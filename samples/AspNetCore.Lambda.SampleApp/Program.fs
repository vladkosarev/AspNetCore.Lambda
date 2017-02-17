﻿// Learn more about F# at http://fsharp.org

open System
open System.IO
open System.Security.Claims
open System.Collections.Generic
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Hosting
open Microsoft.AspNetCore.Http
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open AspNetCore.Lambda.HttpHandlers
open AspNetCore.Lambda.Middleware
open AspNetCore.Lambda.SampleApp

// Error Handler

let errorHandler (ex : Exception) (ctx : HttpHandlerContext) =
    let loggerFactory = ctx.Services.GetService<ILoggerFactory>()
    let logger = loggerFactory.CreateLogger "ErrorHandlerLogger"
    logger.LogError(EventId(0), ex, "An unhandled exception has occurred while executing the request")
    ctx |> (clearResponse >>= setStatusCode 500 >>= text ex.Message)

// Web application

let authScheme = "Cookie"

let accessDenied = setStatusCode 401 >>= text "Access Denied"

let mustBeUser = requiresAuthentication accessDenied

let mustBeAdmin = 
    requiresAuthentication accessDenied 
    >>= requiresRole "Admin" accessDenied

let loginHandler =
    fun ctx ->
        async {
            let issuer = "http://localhost:5000"
            let claims =
                [
                    Claim(ClaimTypes.Name,      "John",  ClaimValueTypes.String, issuer)
                    Claim(ClaimTypes.Surname,   "Doe",   ClaimValueTypes.String, issuer)
                    Claim(ClaimTypes.Role,      "Admin", ClaimValueTypes.String, issuer)
                ]
            let identity = ClaimsIdentity(claims, authScheme)
            let user     = ClaimsPrincipal(identity)

            do! ctx.HttpContext.Authentication.SignInAsync(authScheme, user) |> Async.AwaitTask
            
            return! text "Successfully logged in" ctx
        }

let userHandler =
    fun ctx ->
        text ctx.HttpContext.User.Identity.Name ctx

let showUserHandler id =
    fun ctx ->
        mustBeAdmin >>=
        text (sprintf "User ID: %i" id)
        <| ctx

let webApp = 
    choose [
        GET >>=
            choose [
                route  "/"           >>= text "index"
                route  "/ping"       >>= text "pong"
                route  "/error"      >>= (fun _ -> failwith "Something went wrong!")
                route  "/login"      >>= loginHandler
                route  "/logout"     >>= signOff authScheme >>= text "Successfully logged out."
                route  "/user"       >>= mustBeUser >>= userHandler
                routef "/user/%i"    showUserHandler
                route  "/razor"      >>= razorView "Person.cshtml" { Name = "Razor" }
            ]
        setStatusCode 404 >>= text "Not Found" ]

type Startup() =
    member __.ConfigureServices (services : IServiceCollection, env : IHostingEnvironment) =        
        let viewsFolderPath = Path.Combine(env.WebRootPath, "views")

        services.AddAuthentication() |> ignore
        services.AddDataProtection() |> ignore
        services.AddRazorEngine(viewsFolderPath) |> ignore


    member __.Configure (app : IApplicationBuilder)
                        (env : IHostingEnvironment)
                        (loggerFactory : ILoggerFactory) =
        loggerFactory.AddConsole().AddDebug() |> ignore
        app.UseLambdaErrorHandler(errorHandler)
        app.UseCookieAuthentication(
            new CookieAuthenticationOptions(
                AuthenticationScheme    = authScheme,
                AutomaticAuthenticate   = true,
                AutomaticChallenge      = false,
                CookieHttpOnly          = true,
                CookieSecure            = CookieSecurePolicy.SameAsRequest,
                SlidingExpiration       = true,
                ExpireTimeSpan          = TimeSpan.FromDays 7.0
        )) |> ignore
        app.UseLambda(webApp)


[<EntryPoint>]
let main argv = 
    let host =
        WebHostBuilder()
            .UseKestrel()
            .UseContentRoot(Directory.GetCurrentDirectory())
            .UseStartup<Startup>()
            .Build()
    host.Run()
    0