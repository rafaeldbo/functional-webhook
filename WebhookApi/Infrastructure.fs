module Infrastructure

open System
open System.IO
open System.Net.Http
open System.Text
open System.Text.Json
open System.Collections.Concurrent
open Microsoft.Data.Sqlite
open Models
open Domain

let GatewayUrl = "http://127.0.0.1:5001"

// Lista em memória segura para concorrência simulando o banco de dados.
let HttpClient = new HttpClient()

let IsConfirmed (txId: string) : bool =
    try
        let dbPath = Path.Combine(AppContext.BaseDirectory, "transactions.db")
        use connection = new SqliteConnection($"Data Source={dbPath}")
        connection.Open()

        use command = connection.CreateCommand()
        command.CommandText <- "SELECT COUNT(1) FROM Transactions WHERE TransactionId = @TransactionId"
        command.Parameters.AddWithValue("@TransactionId", txId) |> ignore

        match command.ExecuteScalar() with
        | :? int64 as n -> n > 0L
        | :? int as n -> n > 0
        | _ -> false
    with
    | _ -> false

let cancelTransaction (txId: string) = task {
    let payload = {| transaction_id = txId |}
    let json = JsonSerializer.Serialize payload
    use content = new StringContent(json, Encoding.UTF8, "application/json")
    let! _ = HttpClient.PostAsync($"{GatewayUrl}/cancelar", content)
    return ()
}

let confirmTransaction (txId: string) = task {
    let payload = {| transaction_id = txId |}
    let json = JsonSerializer.Serialize payload
    use content = new StringContent(json, Encoding.UTF8, "application/json")
    let! _ = HttpClient.PostAsync($"{GatewayUrl}/confirmar", content)
    return ()
}

let DatabasePath = Path.Combine(AppContext.BaseDirectory, "transactions.db")
let ConnectionString = $"Data Source={DatabasePath}"

// Cria o banco de dados e a tabela de forma síncrona
let createDatabase () =
    use connection = new SqliteConnection(ConnectionString)
    connection.Open()

    use command = connection.CreateCommand()
    command.CommandText <- "
        CREATE TABLE IF NOT EXISTS Transactions (
            TransactionId TEXT PRIMARY KEY,
            Event TEXT NOT NULL,
            Amount REAL NOT NULL,
            Currency TEXT NOT NULL,
            Timestamp TEXT NOT NULL
        )"

    command.ExecuteNonQuery() |> ignore

// Salva o registro no banco de dados de forma síncrona
let saveTransaction (transaction: TransactionData) : Result<unit, WebhookError> =
    try
        use connection = new SqliteConnection(ConnectionString)
        connection.Open()

        use command = connection.CreateCommand()
        command.CommandText <- "
            INSERT INTO Transactions (TransactionId, Event, Amount, Currency, Timestamp)
            VALUES (@TransactionId, @Event, @Amount, @Currency, @Timestamp)"

        command.Parameters.AddWithValue("@TransactionId", transaction.TransactionId) |> ignore
        command.Parameters.AddWithValue("@Event", transaction.Event) |> ignore
        command.Parameters.AddWithValue("@Amount", transaction.Amount) |> ignore
        command.Parameters.AddWithValue("@Currency", transaction.Currency) |> ignore
        command.Parameters.AddWithValue("@Timestamp", transaction.Timestamp.ToString "O") |> ignore 

        command.ExecuteNonQuery() |> ignore
        Ok ()
    with
        | ex ->
            txError 500 (sprintf "Erro ao salvar transação: %s" ex.Message) transaction.TransactionId