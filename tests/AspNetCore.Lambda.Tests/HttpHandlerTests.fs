module AspNetCore.Lambda.HttpHandlerTests

open System
open System.Collections.Generic
open System.IO
open System.Text
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Primitives
open Microsoft.Extensions.Logging
open Xunit
open NSubstitute
open RazorLight
open AspNetCore.Lambda.HttpHandlers
open AspNetCore.Lambda.Tests

// ---------------------------------
// Helper functions
// ---------------------------------

let getBody (ctx : HttpHandlerContext) =
    ctx.HttpContext.Response.Body.Position <- 0L
    use reader = new StreamReader(ctx.HttpContext.Response.Body, Encoding.UTF8)
    reader.ReadToEnd()

let getContentType (response : HttpResponse) =
    response.Headers.["Content-Type"].[0]

let assertFail msg = Assert.True(false, msg)

let assertFailf format args = 
    let msg = sprintf format args
    Assert.True(false, msg)

// ---------------------------------
// Mocks
// ---------------------------------

let ctx      = Substitute.For<HttpContext>()
let services = Substitute.For<IServiceProvider>()

// ---------------------------------
// Test Types
// ---------------------------------

type Dummy =
    {
        Foo : string
        Bar : string
        Age : int
    }

// ---------------------------------
// Tests
// ---------------------------------

[<Fact>]
let ``GET "/" returns "Hello World"`` () =
    let app = 
        GET >>= choose [ 
            route "/"    >>= text "Hello World"
            route "/foo" >>= text "bar"
            setStatusCode 404 >>= text "Not found" ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Hello World"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``GET "/foo" returns "bar"`` () =
    let app = 
        GET >>= choose [ 
            route "/"    >>= text "Hello World"
            route "/foo" >>= text "bar"
            setStatusCode 404 >>= text "Not found" ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "bar"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``GET "/FOO" returns 404 "Not found"`` () =
    let app = 
        GET >>= choose [ 
            route "/"    >>= text "Hello World"
            route "/foo" >>= text "bar"
            setStatusCode 404 >>= text "Not found" ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/FOO")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Not found"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)
        Assert.Equal(404, ctx.HttpContext.Response.StatusCode)

[<Fact>]
let ``GET "/json" returns json object`` () =
    let app = 
        GET >>= choose [ 
            route "/"     >>= text "Hello World"
            route "/foo"  >>= text "bar"
            route "/json" >>= json { Foo = "john"; Bar = "doe"; Age = 30 }
            setStatusCode 404 >>= text "Not found" ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/json")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "{\"Foo\":\"john\",\"Bar\":\"doe\",\"Age\":30}"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``POST "/post/1" returns "1"`` () =
    let app = 
        choose [
            GET >>= choose [ 
                route "/"     >>= text "Hello World"
                route "/foo"  >>= text "bar" ]
            POST >>= choose [
                route "/post/1" >>= text "1"
                route "/post/2" >>= text "2" ]
            setStatusCode 404 >>= text "Not found" ]

    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/post/1")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "1"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``POST "/post/2" returns "2"`` () =
    let app = 
        choose [
            GET >>= choose [ 
                route "/"     >>= text "Hello World"
                route "/foo"  >>= text "bar" ]
            POST >>= choose [
                route "/post/1" >>= text "1"
                route "/post/2" >>= text "2" ]
            setStatusCode 404 >>= text "Not found" ]

    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/post/2")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "2"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``PUT "/post/2" returns 404 "Not found"`` () =
    let app = 
        choose [
            GET >>= choose [ 
                route "/"     >>= text "Hello World"
                route "/foo"  >>= text "bar" ]
            POST >>= choose [
                route "/post/1" >>= text "1"
                route "/post/2" >>= text "2" ]
            setStatusCode 404 >>= text "Not found" ]
    
    ctx.Request.Method.ReturnsForAnyArgs "PUT" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/post/2")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Not found"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)
        Assert.Equal(404, ctx.HttpContext.Response.StatusCode)

[<Fact>]
let ``GET "/dotLiquid" returns rendered html view`` () =
    let dotLiquidTemplate =
        "<html><head><title>DotLiquid</title></head>" + 
        "<body><p>{{ foo }} {{ bar }} is {{ age }} years old.</p>" +
        "</body></html>"

    let obj = { Foo = "John"; Bar = "Doe"; Age = 30 }

    let app = 
        choose [
            GET >>= choose [ 
                route "/"          >>= text "Hello World"
                route "/dotLiquid" >>= dotLiquid "text/html" dotLiquidTemplate obj ]
            POST >>= choose [
                route "/post/1" >>= text "1" ]
            setStatusCode 404 >>= text "Not found" ]
    
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/dotLiquid")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "<html><head><title>DotLiquid</title></head><body><p>John Doe is 30 years old.</p></body></html>"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)
        Assert.Equal("text/html", ctx.HttpContext.Response |> getContentType)

[<Fact>]
let ``POST "/text" with supported Accept header returns "good"`` () =
    let app = 
        choose [
            GET >>= choose [ 
                route "/"     >>= text "Hello World"
                route "/foo"  >>= text "bar" ]
            POST >>= choose [
                route "/text"   >>= mustAccept [ "text/plain" ] >>= text "text"
                route "/json"   >>= mustAccept [ "application/json" ] >>= json "json"
                route "/either" >>= mustAccept [ "text/plain"; "application/json" ] >>= text "either" ]
            setStatusCode 404 >>= text "Not found" ]
    
    let headers = new HeaderDictionary()
    headers.Add("Accept", new StringValues("text/plain"))
    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/text")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "text"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)
        Assert.Equal("text/plain", ctx.HttpContext.Response |> getContentType)

[<Fact>]
let ``POST "/json" with supported Accept header returns "json"`` () =
    let app = 
        choose [
            GET >>= choose [ 
                route "/"     >>= text "Hello World"
                route "/foo"  >>= text "bar" ]
            POST >>= choose [
                route "/text"   >>= mustAccept [ "text/plain" ] >>= text "text"
                route "/json"   >>= mustAccept [ "application/json" ] >>= json "json"
                route "/either" >>= mustAccept [ "text/plain"; "application/json" ] >>= text "either" ]
            setStatusCode 404 >>= text "Not found" ]

    let headers = new HeaderDictionary()
    headers.Add("Accept", new StringValues("application/json"))
    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/json")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "\"json\""

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)
        Assert.Equal("application/json", ctx.HttpContext.Response |> getContentType)

[<Fact>]
let ``POST "/either" with supported Accept header returns "either"`` () =
    let app = 
        choose [
            GET >>= choose [ 
                route "/"     >>= text "Hello World"
                route "/foo"  >>= text "bar" ]
            POST >>= choose [
                route "/text"   >>= mustAccept [ "text/plain" ] >>= text "text"
                route "/json"   >>= mustAccept [ "application/json" ] >>= json "json"
                route "/either" >>= mustAccept [ "text/plain"; "application/json" ] >>= text "either" ]
            setStatusCode 404 >>= text "Not found" ]

    let headers = new HeaderDictionary()
    headers.Add("Accept", new StringValues("application/json"))
    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/either")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "either"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)
        Assert.Equal("text/plain", ctx.HttpContext.Response |> getContentType)

[<Fact>]
let ``POST "/either" with unsupported Accept header returns 404 "Not found"`` () =
    let app = 
        choose [
            GET >>= choose [ 
                route "/"     >>= text "Hello World"
                route "/foo"  >>= text "bar" ]
            POST >>= choose [
                route "/text"   >>= mustAccept [ "text/plain" ] >>= text "text"
                route "/json"   >>= mustAccept [ "application/json" ] >>= json "json"
                route "/either" >>= mustAccept [ "text/plain"; "application/json" ] >>= text "either" ]
            setStatusCode 404 >>= text "Not found" ]

    let headers = new HeaderDictionary()
    headers.Add("Accept", new StringValues("application/xml"))
    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/either")) |> ignore
    ctx.Request.Headers.ReturnsForAnyArgs(headers) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Not found"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)
        Assert.Equal(404, ctx.HttpContext.Response.StatusCode)

[<Fact>]
let ``GET "/JSON" returns "BaR"`` () =
    let app =
        GET >>= choose [ 
            route   "/"       >>= text "Hello World"
            route   "/foo"    >>= text "bar"
            route   "/json"   >>= json { Foo = "john"; Bar = "doe"; Age = 30 }
            routeCi "/json"   >>= text "BaR"
            setStatusCode 404 >>= text "Not found" ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/JSON")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "BaR"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``GET "/foo/blah blah/bar" returns "blah blah"`` () =
    let app =
        GET >>= choose [ 
            route   "/"       >>= text "Hello World"
            route   "/foo"    >>= text "bar"
            routef "/foo/%s/bar" text
            routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
            setStatusCode 404 >>= text "Not found" ]

    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo/blah blah/bar")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "blah%20blah"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``GET "/foo/johndoe/59" returns "Name: johndoe, Age: 59"`` () =
    let app =
        GET >>= choose [ 
            route   "/"       >>= text "Hello World"
            route   "/foo"    >>= text "bar"
            routef "/foo/%s/bar" text
            routef "/foo/%s/%i" (fun (name, age) -> text (sprintf "Name: %s, Age: %d" name age))
            setStatusCode 404 >>= text "Not found" ]
    
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/foo/johndoe/59")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "Name: johndoe, Age: 59"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``POST "/POsT/1" returns "1"`` () =
    let app =
        choose [
            GET >>= choose [ 
                route "/"          >>= text "Hello World" ]
            POST >>= choose [
                route    "/post/1" >>= text "1"
                routeCif "/post/%i" json ]
            setStatusCode 404 >>= text "Not found" ]

    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/POsT/1")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "1"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``POST "/POsT/523" returns "523"`` () =
    let app =
        choose [
            GET >>= choose [ 
                route "/"          >>= text "Hello World" ]
            POST >>= choose [
                route    "/post/1" >>= text "1"
                routeCif "/post/%i" json ]
            setStatusCode 404 >>= text "Not found" ]

    ctx.Request.Method.ReturnsForAnyArgs "POST" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/POsT/523")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "523"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None     -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``GET "/api" returns "api root"`` () =
    let app = 
        GET >>= choose [
            route "/"    >>= text "Hello World"
            route "/foo" >>= text "bar"
            subRoute "/api" (
                choose [
                    route ""       >>= text "api root"
                    route "/admin" >>= text "admin"
                    route "/users" >>= text "users" ] )
            route "/api/test" >>= text "test"
            setStatusCode 404 >>= text "Not found" ]

    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/api")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "api root"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None          -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``GET "/api/users" returns "users"`` () =
    let app = 
        GET >>= choose [
            route "/"    >>= text "Hello World"
            route "/foo" >>= text "bar"
            subRoute "/api" (
                choose [
                    route ""       >>= text "api root"
                    route "/admin" >>= text "admin"
                    route "/users" >>= text "users" ] )
            route "/api/test" >>= text "test"
            setStatusCode 404 >>= text "Not found" ]
    
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/users")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "users"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None          -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``GET "/api/test" returns "test"`` () =
    let app = 
        GET >>= choose [
            route "/"    >>= text "Hello World"
            route "/foo" >>= text "bar"
            subRoute "/api" (
                choose [
                    route ""       >>= text "api root"
                    route "/admin" >>= text "admin"
                    route "/users" >>= text "users" ] )
            route "/api/test" >>= text "test"
            setStatusCode 404 >>= text "Not found" ]

    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/test")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "test"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None          -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``GET "/api/v2/users" returns "users v2"`` () =
    let app = 
        GET >>= choose [
            route "/"    >>= text "Hello World"
            route "/foo" >>= text "bar"
            subRoute "/api" (
                choose [
                    route ""       >>= text "api root"
                    route "/admin" >>= text "admin"
                    route "/users" >>= text "users"
                    subRoute "/v2" (
                        choose [
                            route ""       >>= text "api root v2"
                            route "/admin" >>= text "admin v2"
                            route "/users" >>= text "users v2"
                        ]
                    )
                ]
            )
            route "/api/test" >>= text "test"
            setStatusCode 404 >>= text "Not found" ]
    
    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/v2/users")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "users v2"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None          -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``GET "/api/foo/bar/yadayada" returns "yadayada"`` () =
    let app = 
        GET >>= choose [
            route "/"    >>= text "Hello World"
            route "/foo" >>= text "bar"
            subRoute "/api" (
                choose [
                    route  "" >>= text "api root"
                    routef "/foo/bar/%s" text ] )
            route "/api/test" >>= text "test"
            setStatusCode 404 >>= text "Not found" ]

    ctx.Items.Returns (new Dictionary<obj,obj>() :> IDictionary<obj,obj>) |> ignore
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/api/foo/bar/yadayada")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "yadayada"

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None          -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)

[<Fact>]
let ``GET "/razor" returns rendered html view`` () =
    let app = 
        GET >>= choose [ 
            route "/"      >>= text "Hello World"
            route "/foo"   >>= text "bar"
            route "/razor" >>= razorView "Person" { Name = "razor" }
            setStatusCode 404 >>= text "Not found" ]
    
    services.GetService(typeof<IRazorLightEngine>).Returns(EngineFactory.CreateEmbedded(typeof<Person>))
    ctx.Request.Method.ReturnsForAnyArgs "GET" |> ignore
    ctx.Request.Path.ReturnsForAnyArgs (PathString("/razor")) |> ignore
    ctx.Response.Body <- new MemoryStream()
    let expected = "<html><head><title>Hello, razor</title></head><body><h3>Hello, razor</h3></body></html>"    

    let result = 
        { HttpContext = ctx; Services = services }
        |> app
        |> Async.RunSynchronously

    match result with
    | None          -> assertFailf "Result was expected to be %s" expected
    | Some ctx ->
        let body = getBody ctx
        Assert.Equal(expected, body)
        Assert.Equal("text/html", ctx.HttpContext.Response |> getContentType)