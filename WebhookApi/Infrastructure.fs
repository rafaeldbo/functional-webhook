module Infrastructure

open System.Net.Http
open System.Text
open System.Text.Json
open System.Collections.Concurrent

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
