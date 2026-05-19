module Models

type WebhookError =
    | EarlyError of StatusCode: int * Response: obj
    | TransactionError of StatusCode: int * Response: obj * TxId: string
