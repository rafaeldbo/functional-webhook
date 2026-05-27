module Models

open System

type WebhookError =
    | EarlyError of StatusCode: int * Response: obj
    | TransactionError of StatusCode: int * Response: obj * TxId: string

type TransactionData = {
    TransactionId: string
    Event: string
    Amount: float
    Currency: string
    Timestamp: DateTime
}
