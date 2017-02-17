module AspNetCore.Lambda.Middleware

open System
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Logging
open Microsoft.Extensions.DependencyInjection
open AspNetCore.Lambda.HttpHandlers
open RazorLight

type LambdaMiddleware (next     : RequestDelegate,
                       handler  : HttpHandler,
                       services : IServiceProvider) =
    
    do if isNull next then raise (ArgumentNullException("next"))

    member __.Invoke (ctx : HttpContext) =
        async {
            let httpHandlerContext =
                {
                    HttpContext = ctx
                    Services    = services
                }
            let! result = handler httpHandlerContext
            if (result.IsNone) then
                return!
                    next.Invoke ctx
                    |> Async.AwaitTask
        } |> Async.StartAsTask

type LambdaErrorHandlerMiddleware (next          : RequestDelegate,
                                   errorHandler  : ErrorHandler,
                                   services      : IServiceProvider) =

    do if isNull next then raise (ArgumentNullException("next"))

    member __.Invoke (ctx : HttpContext) =
        async {
            try
                return!
                    next.Invoke ctx
                    |> Async.AwaitTask
            with ex ->
                try
                    let httpHandlerContext =
                        {
                            HttpContext = ctx
                            Services    = services
                        }
                    return!
                        errorHandler ex httpHandlerContext
                        |> Async.Ignore
                with ex2 ->
                    let logger = services.GetService<ILogger<LambdaErrorHandlerMiddleware>>()
                    logger.LogError(EventId(0), ex, "An unhandled exception has occurred while executing the request.")
                    logger.LogError(EventId(0), ex2, "An exception was thrown attempting to handle the original exception.")
        } |> Async.StartAsTask

type IApplicationBuilder with
    member this.UseLambda (handler : HttpHandler) =
        this.UseMiddleware<LambdaMiddleware>(handler)
        |> ignore

    member this.UseLambdaErrorHandler (handler : ErrorHandler) =
        this.UseMiddleware<LambdaErrorHandlerMiddleware>(handler)
        |> ignore

type IServiceCollection with
    member this.AddRazorEngine (viewsFolderPath : string) =
        this.AddSingleton<IRazorLightEngine>(EngineFactory.CreatePhysical(viewsFolderPath));