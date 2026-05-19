module Domain

open System.Text.Json
open System.Text.Json.Nodes
open Models

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