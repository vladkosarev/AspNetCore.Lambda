module AspNetCore.Lambda.HttpHandlers

open System
open System.Text
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Primitives
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open FSharp.Core.Printf
open Newtonsoft.Json
open DotLiquid
open RazorLight
open AspNetCore.Lambda.Common
open AspNetCore.Lambda.FormatExpressions

type HttpHandlerContext =
    {
        HttpContext : HttpContext
        Services    : IServiceProvider
    }

type HttpHandler = HttpHandlerContext -> Async<HttpHandlerContext option>

type ErrorHandler = exn -> HttpHandler

/// ---------------------------
/// Private helper functions
/// ---------------------------

let private strOption (str : string) =
    if String.IsNullOrEmpty str then None else Some str

[<Literal>]
let private RouteKey = "aspnet_lambda_route"

let private getSavedSubPath (ctx : HttpContext) =
    if ctx.Items.ContainsKey RouteKey
    then ctx.Items.Item RouteKey |> string |> strOption 
    else None

let private getPath (ctx : HttpContext) =
    match getSavedSubPath ctx with
    | Some p -> ctx.Request.Path.ToString().[p.Length..]
    | None   -> ctx.Request.Path.ToString()

let private handlerWithRootedPath (path : string) (handler : HttpHandler) = 
    fun (ctx : HttpHandlerContext) ->
        async {
            let savedSubPath = getSavedSubPath ctx.HttpContext
            try
                ctx.HttpContext.Items.Item RouteKey <- ((savedSubPath |> Option.defaultValue "") + path)
                return! handler ctx
            finally
                match savedSubPath with
                | Some savedSubPath -> ctx.HttpContext.Items.Item RouteKey <- savedSubPath
                | None              -> ctx.HttpContext.Items.Remove RouteKey |> ignore
        }

/// ---------------------------
/// Default HttpHandlers
/// ---------------------------

/// Combines two HttpHandler functions into one.
/// If the first HttpHandler returns Some HttpContext, then it will proceed to
/// the second handler, otherwise short circuit and return None as the final result.
/// If the first HttpHandler returned Some HttpResult, but the response has already 
/// been written, then it will return its result and skip the second HttpHandler as well.
let bind (handler : HttpHandler) (handler2 : HttpHandler) =
    fun (ctx : HttpHandlerContext) ->
        async {
            let! result = handler ctx
            match result with
            | None      -> return None
            | Some ctx2 ->
                match ctx2.HttpContext.Response.HasStarted with
                | true  -> return  Some ctx2
                | false -> return! handler2 ctx2
        }

/// Combines two HttpHandler functions into one.
/// See bind for more information.
let (>>=) = bind

/// Iterates through a list of HttpHandler functions and returns the
/// result of the first HttpHandler which outcome is Some HttpContext
let rec choose (handlers : HttpHandler list) =
    fun (ctx : HttpHandlerContext) ->
        async {
            match handlers with
            | []                -> return None
            | handler :: tail   ->
                let! result = handler ctx
                match result with
                | Some c    -> return Some c
                | None      -> return! choose tail ctx
        }

/// Filters an incoming HTTP request based on the HTTP verb
let httpVerb (verb : string) =
    fun (ctx : HttpHandlerContext) ->
        if ctx.HttpContext.Request.Method.Equals verb
        then Some ctx
        else None
        |> async.Return

let GET     = httpVerb "GET"    : HttpHandler
let POST    = httpVerb "POST"   : HttpHandler
let PUT     = httpVerb "PUT"    : HttpHandler
let PATCH   = httpVerb "PATCH"  : HttpHandler
let DELETE  = httpVerb "DELETE" : HttpHandler

/// Filters an incoming HTTP request based on the accepted
/// mime types of the client.
let mustAccept (mimeTypes : string list) =
    fun (ctx : HttpHandlerContext) ->
        let headers = ctx.HttpContext.Request.GetTypedHeaders()
        headers.Accept
        |> Seq.map    (fun h -> h.ToString())
        |> Seq.exists (fun h -> mimeTypes |> Seq.contains h)
        |> function
            | true  -> Some ctx
            | false -> None
            |> async.Return          

/// Challenges the client to authenticate with a given authentication scheme.
let challenge (authScheme : string) =
    fun (ctx : HttpHandlerContext) ->
        async {
            let auth = ctx.HttpContext.Authentication
            do! auth.ChallengeAsync authScheme |> Async.AwaitTask
            return Some ctx
        }

/// Signs off the current user.
let signOff (authScheme : string) =
    fun (ctx : HttpHandlerContext) ->
        async {
            let auth = ctx.HttpContext.Authentication
            do! auth.SignOutAsync authScheme |> Async.AwaitTask
            return Some ctx
        }

/// Validates if a user is authenticated.
/// If not it will proceed with the authFailedHandler.
let requiresAuthentication (authFailedHandler : HttpHandler) =
    fun (ctx : HttpHandlerContext) ->
        let user = ctx.HttpContext.User
        if isNotNull user && user.Identity.IsAuthenticated
        then async.Return (Some ctx)
        else authFailedHandler ctx

/// Validates if a user is in a specific role.
/// If not it will proceed with the authFailedHandler.
let requiresRole (role : string) (authFailedHandler : HttpHandler) =
    fun (ctx : HttpHandlerContext) ->
        let user = ctx.HttpContext.User
        if user.IsInRole role
        then async.Return (Some ctx)
        else authFailedHandler ctx

/// Validates if a user has at least one of the specified roles.
/// If not it will proceed with the authFailedHandler.
let requiresRoleOf (roles : string list) (authFailedHandler : HttpHandler) =
    fun (ctx : HttpHandlerContext) ->
        let user = ctx.HttpContext.User
        roles
        |> List.exists user.IsInRole 
        |> function
            | true  -> async.Return (Some ctx)
            | false -> authFailedHandler ctx

/// Attempts to clear the current HttpResponse object.
/// This can be useful inside an error handler when the response
/// needs to be overwritten in the case of a failure.
let clearResponse =
    fun (ctx : HttpHandlerContext) ->
        ctx.HttpContext.Response.Clear()
        async.Return (Some ctx)

/// Filters an incoming HTTP request based on the request path (case sensitive).
let route (path : string) =
    fun (ctx : HttpHandlerContext) ->
        if (getPath ctx.HttpContext).Equals path
        then Some ctx
        else None
        |> async.Return

/// Filters an incoming HTTP request based on the request path (case sensitive).
/// The arguments from the format string will be automatically resolved when the
/// route matches and subsequently passed into the supplied routeHandler.
let routef (path : StringFormat<_, 'T>) (routeHandler : 'T -> HttpHandler) =
    fun (ctx : HttpHandlerContext) ->
        tryMatchInput path (getPath ctx.HttpContext) false
        |> function
            | None      -> async.Return None
            | Some args -> routeHandler args ctx

/// Filters an incoming HTTP request based on the request path (case insensitive).
let routeCi (path : string) =
    fun (ctx : HttpHandlerContext) ->
        if String.Equals(getPath ctx.HttpContext, path, StringComparison.CurrentCultureIgnoreCase)
        then Some ctx
        else None
        |> async.Return

/// Filters an incoming HTTP request based on the request path (case insensitive).
/// The arguments from the format string will be automatically resolved when the
/// route matches and subsequently passed into the supplied routeHandler.
let routeCif (path : StringFormat<_, 'T>) (routeHandler : 'T -> HttpHandler) =
    fun (ctx : HttpHandlerContext) ->
        tryMatchInput path (getPath ctx.HttpContext) true
        |> function
            | None      -> None |> async.Return
            | Some args -> routeHandler args ctx

/// Filters an incoming HTTP request based on the beginning of the request path (case sensitive).
let routeStartsWith (subPath : string) =
    fun (ctx : HttpHandlerContext) ->
        if (getPath ctx.HttpContext).StartsWith subPath 
        then Some ctx
        else None
        |> async.Return

/// Filters an incoming HTTP request based on the beginning of the request path (case insensitive).
let routeStartsWithCi (subPath : string) =
    fun (ctx : HttpHandlerContext) ->
        if (getPath ctx.HttpContext).StartsWith(subPath, StringComparison.CurrentCultureIgnoreCase) 
        then Some ctx
        else None
        |> async.Return

/// Filters an incoming HTTP request based on a part of the request path (case sensitive).
/// Subsequent route handlers inside the given handler function should omit the already validated path.
let subRoute (path : string) (handler : HttpHandler) =
    routeStartsWith path >>=
    handlerWithRootedPath path handler

/// Filters an incoming HTTP request based on a part of the request path (case insensitive).
/// Subsequent route handlers inside the given handler function should omit the already validated path.
let subRouteCi (path : string) (handler : HttpHandler) =
    routeStartsWithCi path >>=
    handlerWithRootedPath path handler


/// Sets the HTTP response status code.
let setStatusCode (statusCode : int) =
    fun (ctx : HttpHandlerContext) ->
        async {
            ctx.HttpContext.Response.StatusCode <- statusCode
            return Some ctx
        }

/// Sets a HTTP header in the HTTP response.
let setHttpHeader (key : string) (value : obj) =
    fun (ctx : HttpHandlerContext) ->
        async {
            ctx.HttpContext.Response.Headers.[key] <- new StringValues(value.ToString())
            return Some ctx
        }

/// Writes to the body of the HTTP response and sets the HTTP header Content-Length accordingly.
let setBody (bytes : byte array) =
    fun (ctx : HttpHandlerContext) ->
        async {            
            ctx.HttpContext.Response.Headers.["Content-Length"] <- new StringValues(bytes.Length.ToString())
            ctx.HttpContext.Response.Body.WriteAsync(bytes, 0, bytes.Length)
            |> Async.AwaitTask
            |> ignore
            return Some ctx
        }

/// Writes a string to the body of the HTTP response and sets the HTTP header Content-Length accordingly.
let setBodyAsString (str : string) =
    Encoding.UTF8.GetBytes str
    |> setBody

/// Writes a string to the body of the HTTP response.
/// It also sets the HTTP header Content-Type: text/plain and sets the Content-Length header accordingly.
let text (str : string) =
    setHttpHeader "Content-Type" "text/plain"
    >>= setBodyAsString str

/// Serializes an object to JSON and writes it to the body of the HTTP response.
/// It also sets the HTTP header Content-Type: application/json and sets the Content-Length header accordingly.
let json (dataObj : obj) =
    setHttpHeader "Content-Type" "application/json"
    >>= setBodyAsString (JsonConvert.SerializeObject dataObj)

/// Serializes an object to XML and writes it to the body of the HTTP response.
/// It also sets the HTTP header Content-Type: application/xml and sets the Content-Length header accordingly.
let xml (dataObj : obj) =
    setHttpHeader "Content-Type" "application/xml"
    >>= setBody (serializeXml dataObj)

/// Renders a model and a template with the DotLiquid template engine and sets the HTTP response
/// with the compiled output as well as the Content-Type HTTP header to the given value.
let dotLiquid (contentType : string) (template : string) (model : obj) =
    let view = Template.Parse template
    setHttpHeader "Content-Type" contentType
    >>= (model
        |> Hash.FromAnonymousObject
        |> view.Render
        |> setBodyAsString)

/// Renders a model and a HTML template with the DotLiquid template engine and sets the HTTP response
/// with the compiled output as well as the Content-Type HTTP header to text/html.
let htmlTemplate (relativeTemplatePath : string) (model : obj) = 
    fun (ctx : HttpHandlerContext) ->
        async {
            let env = ctx.Services.GetService<IHostingEnvironment>()
            let templatePath = env.ContentRootPath + relativeTemplatePath
            let! template = readFileAsString templatePath
            return! dotLiquid "text/html" template model ctx
        }

/// Reads a HTML file from disk and writes its contents to the body of the HTTP response
/// with a Content-Type of text/html.
let htmlFile (relativeFilePath : string) =
    fun (ctx : HttpHandlerContext) ->
        async {
            let env = ctx.Services.GetService<IHostingEnvironment>()
            let filePath = env.ContentRootPath + relativeFilePath
            let! html = readFileAsString filePath
            return!
                ctx
                |> (setHttpHeader "Content-Type" "text/html"
                >>= setBodyAsString html)
        }

/// Parses and compiles a Razor view with the associated model and then writes its contents
/// to the body of the response. It also sets the HTTP header Content-Type to text/html.
let razorView (viewName : string) (model : obj) =
    fun (ctx : HttpHandlerContext) ->
        let engine = ctx.Services.GetService<IRazorLightEngine>()
        let view = engine.Parse(viewName, model)
        setHttpHeader "Content-Type" "text/html"
        >>= setBodyAsString view
        <| ctx