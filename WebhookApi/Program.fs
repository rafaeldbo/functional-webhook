open System
open System.IO
open System.Text
open System.Text.Json
open System.Text.Json.Nodes
open System.Net.Http
open System.Collections.Concurrent
open Microsoft.AspNetCore.Builder
open Microsoft.AspNetCore.Http
open Microsoft.AspNetCore.Hosting
open Microsoft.Extensions.Hosting

module Domain =

    type WebhookError =
        | EarlyError of StatusCode: int * Response: obj
        | TransactionError of StatusCode: int * Response: obj * TxId: string

    let private baseError code reason = 
        Error (EarlyError(code, {| status = "cancelled"; reason = reason |}))

    let private txError code reason txId = 
        Error (TransactionError(code, {| status = "cancelled"; reason = reason; transaction_id = txId |}, txId))

    let ValidateToken (token: string option) =
        match token with
        | Some "meu-token-secreto" -> Ok ()
        | _ -> baseError 403 "invalid token"

    let ParsePayload (body: string) =
        try
            let node = JsonNode.Parse body
            if node = null then 
                baseError 400 "invalid payload"
            else 
                Ok (node.AsObject())
        with _ ->
            baseError 400 "invalid payload"

    let ValidateTxId (root: JsonObject) =
        if root.ContainsKey "transaction_id"  then
            let node = root.["transaction_id"]
            if node <> null && node.GetValueKind() = JsonValueKind.String then
                Ok (root, node.GetValue<string>())
            else
                baseError 400 "missing field: transaction_id"
        else
            baseError 400 "missing field: transaction_id"

    let ValidateRemainingFields (root: JsonObject, txId: string) =
        let requiredKeys = [ "event"; "amount"; "currency"; "timestamp" ]
        let missingKey = 
            requiredKeys 
            |> List.tryFind (fun k -> not (root.ContainsKey k))

        match missingKey with
        | Some key -> txError 400 (sprintf "missing field: %s" key) txId
        | None -> Ok (root, txId)

    let ValidateNotConfirmed (isConfirmed: string -> bool) (root: JsonObject, txId: string) =
        if isConfirmed txId then
            txError 400 "transaction duplicated" txId
        else 
            Ok (root, txId)

    let ValidateOrder (root: JsonObject, txId: string) =
        let tryGetString propName =
            if root.ContainsKey propName  then
                let node = root.[propName]
                if node <> null && node.GetValueKind() = JsonValueKind.String then Some (node.GetValue<string>())
                else None
            else None

        let amount = tryGetString "amount"
        let currency = tryGetString "currency"

        if amount <> Some "49.90" || currency <> Some "BRL" then
            txError 400 "mismatch" txId
        else 
            Ok txId

    let (>>=) result func = Result.bind func result

    let ProcessWebhook token body isConfirmed =
        ValidateToken token
        >>= fun _ -> ParsePayload body
        >>= ValidateTxId
        >>= ValidateRemainingFields
        >>= ValidateNotConfirmed isConfirmed
        >>= ValidateOrder


module Infrastructure =

    let GatewayUrl = "http://127.0.0.1:5001"
    
    // Lista em memória segura para concorrência simulando o banco de dados.
    let Confirmations = ConcurrentDictionary<string, bool>()
    let HttpClient = new HttpClient()

    let IsConfirmed txId = Confirmations.ContainsKey(txId)
    let MarkConfirmed txId = Confirmations.TryAdd(txId, true) |> ignore

    let CancelTransaction (txId: string) = task {
        let payload = {| transaction_id = txId |}
        let json = JsonSerializer.Serialize payload
        use content = new StringContent(json, Encoding.UTF8, "application/json")
        let! _ = HttpClient.PostAsync($"{GatewayUrl}/cancelar", content)
        return ()
    }

    let ConfirmTransaction (txId: string) = task {
        let payload = {| transaction_id = txId |}
        let json = JsonSerializer.Serialize payload
        use content = new StringContent(json, Encoding.UTF8, "application/json")
        let! _ = HttpClient.PostAsync($"{GatewayUrl}/confirmar", content)
        return ()
    }


module Api =
    open Domain
    open Infrastructure

    let HandleWebhook (ctx: HttpContext) = task {
        use reader = new StreamReader(ctx.Request.Body)
        let! body = reader.ReadToEndAsync()

        let token = 
            match ctx.Request.Headers.TryGetValue "X-Webhook-Token" with
            | true, v -> Some (v.ToString())
            | _ -> None

        // Executa o pipeline puro e trata os efeitos colaterais com base no padrão correspondente.
        match ProcessWebhook token body IsConfirmed with
        | Ok txId ->
            MarkConfirmed txId
            do! ConfirmTransaction txId
            ctx.Response.StatusCode <- 200
            do! ctx.Response.WriteAsJsonAsync {| status = "confirmed"; transaction_id = txId |}

        | Error (EarlyError (statusCode, response)) ->
            ctx.Response.StatusCode <- statusCode
            do! ctx.Response.WriteAsJsonAsync response

        | Error (TransactionError (statusCode, response, txId)) ->
            do! CancelTransaction txId
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

    app.MapPost(
        "/webhook", 
        RequestDelegate(fun ctx -> Api.HandleWebhook ctx :> Threading.Tasks.Task)
    ) |> ignore

    app.Run()
    0