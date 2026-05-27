module Utils

open System
open System.Text.Json
open System.Text.Json.Nodes

let (>>=) result func = Result.bind func result

let parseJson (json: string) =
    try
        let node = JsonNode.Parse json
        if isNull node then 
            Error "json empty"
        else 
            Ok (node.AsObject())
    with _ ->
        Error "invalid json"

let tryGetJsonValue<'T> (obj: JsonObject) (key: string) (kind: JsonValueKind) =
    let exists, node = obj.TryGetPropertyValue key
    if not exists && isNull node then 
        Error (sprintf "missing field: %s" key)
    elif node.GetValueKind() <> kind then
        Error (sprintf "invalid field: %s" key)
    else 
        Ok (node.GetValue<'T>())

let parseStringToDateTime (str: string) =
    match DateTime.TryParse str with
    | true, dt -> Ok dt
    | _ -> Error "invalid timestamp"