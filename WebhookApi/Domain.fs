module Domain


open System.Text.Json
open System.Text.Json.Nodes
open FsToolkit.ErrorHandling
open Utils
open Models

[<Literal>]
let private SecretToken = "meu-token-secreto"

let private baseError code reason = 
    Error (EarlyError(code, {| status = "cancelled"; reason = reason |}))

let txError code reason txId = 
    Error (TransactionError(code, {| status = "cancelled"; reason = reason; transaction_id = txId |}, txId))

let validateToken (token: string option) =
    match token with
    | Some SecretToken -> Ok ()
    | _ -> baseError 403 "invalid token"

let parsePayload (body: string) =
    match parseJson body with
    | Ok jsonObj -> Ok jsonObj
    | Error msg -> baseError 400 msg

let parseOrder (jsonObj: JsonObject) =
    let transactionId = tryGetJsonValue<string> jsonObj "transaction_id" JsonValueKind.String

    let order = result {
        let! event = tryGetJsonValue<string> jsonObj "event" JsonValueKind.String
        let! amount = tryGetJsonValue<float> jsonObj "amount" JsonValueKind.Number
        let! currency = tryGetJsonValue<string> jsonObj "currency" JsonValueKind.String
        let! timestamp = 
            tryGetJsonValue<string> jsonObj "timestamp" JsonValueKind.String
            >>= parseStringToDateTime
        let! transactionId = transactionId
        return {
            TransactionId = transactionId
            Event = event
            Amount = amount
            Currency = currency
            Timestamp = timestamp
        }
    }
    match transactionId with
    | Ok txId -> 
        match order with
        | Ok order -> Ok order
        | Error msg -> txError 400 msg txId
    | Error msg -> baseError 400 msg

let validateNotConfirmed (isConfirmed: string -> bool) (tx: TransactionData) =
    if isConfirmed tx.TransactionId then
        txError 400 "transaction duplicated" tx.TransactionId
    else 
        Ok tx

let validateOrder (data: TransactionData) =
    if data.Amount <> 49.90 || data.Currency <> "BRL" then
        txError 400 "mismatch" data.TransactionId
    else
        Ok data

let processWebhook token body isConfirmed =
    validateToken token
    >>= fun _ -> parsePayload body
    >>= parseOrder
    >>= validateNotConfirmed isConfirmed
    >>= validateOrder