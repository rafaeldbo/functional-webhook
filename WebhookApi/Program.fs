open System
open System.IO
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting

open Infrastructure

module Api =
    open Models
    open Domain

    let HandleWebhook (ctx: HttpContext) = task {
        use reader = new StreamReader(ctx.Request.Body)
        let! body = reader.ReadToEndAsync()

        let token = 
            match ctx.Request.Headers.TryGetValue "X-Webhook-Token" with
            | true, v -> Some (v.ToString())
            | _ -> None

        // Executa o pipeline puro e trata os efeitos colaterais com base no padrão correspondente.
        match processWebhook token body IsConfirmed with
        | Ok transaction ->
            markConfirmed transaction.TransactionId
            do saveTransaction transaction
            do! confirmTransaction transaction.TransactionId
            ctx.Response.StatusCode <- 200
            do! ctx.Response.WriteAsJsonAsync {| status = "confirmed"; transaction_id = transaction.TransactionId |}

        | Error (EarlyError (statusCode, response)) ->
            ctx.Response.StatusCode <- statusCode
            do! ctx.Response.WriteAsJsonAsync response

        | Error (TransactionError (statusCode, response, txId)) ->
            do! cancelTransaction txId
            ctx.Response.StatusCode <- statusCode
            do! ctx.Response.WriteAsJsonAsync response
    }

[<EntryPoint>]
let main args =
    let builder = WebApplication.CreateBuilder args
    builder.WebHost.ConfigureKestrel(fun options ->
        options.ListenLocalhost 5000
        options.ListenLocalhost(5002, fun listenOptions ->
            listenOptions.UseHttps() |> ignore
        )
    ) |> ignore
    let app = builder.Build()

    createDatabase()

    app.MapPost(
        "/webhook", 
        RequestDelegate(fun ctx -> Api.HandleWebhook ctx :> Threading.Tasks.Task)
    ) |> ignore

    app.Run()
    0