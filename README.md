# Functional Webhook API

Serviço de webhook para processamento de eventos de pagamento. Desenvolvido em F#, o sistema aplica conceitos de programação funcional para garantir a integridade das transações, idempotência e isolamento de efeitos colaterais.

## Visão Geral

* **Pré-requisito:** .NET SDK 10.0 e Python 3.8+ (para testes)
* **Instalação de Dependências e Build:**
```bash
dotnet restore WebhookApi.fsproj
dotnet build WebhookApi.fsproj
pip install -r requirements.txt # Um ambiente virtual é recomendado
```


* **Execução do Servidor:**
```bash
dotnet run --project WebhookApi.fsproj
```

O serviço escutará conexões HTTP locais na porta 5000 e conexões HTTPS seguras na porta 5002. O banco de dados SQLite será criado automaticamente na inicialização.

## Arquitetura de Pastas

```text
test_webhook.py       # Suíte de testes automatizada para simulação do gateway e envio dos webhooks
WebhookApi/
├── Domain.fs         # Regras de negócio puras, validação de token, payload e critérios de aceite
├── Infrastructure.fs # Gerenciamento de efeitos colaterais: chamadas HTTP (Gateway) e persistência SQLite
├── Models.fs         # Definição dos tipos de domínio e estruturas de erro estruturado
├── Program.fs        # Configuração do Kestrel, pipeline da aplicação e mapeamento de rotas
└── Utils.fs          # Ferramentas auxiliares para parsing seguro de JSON e manipulação de tipos
```

## Fluxo de Processamento

O sistema implementa um pipeline funcional utilizando monads de avaliação estrita (via `FsToolkit.ErrorHandling`) para processar cada requisição de forma contínua e segura:

1. **Autenticação:** Validação de segurança primária via header `X-Webhook-Token`. Requisições com token ausente ou inválido são sumariamente rejeitadas (Early Error 403), não gerando processamento adjacente.
2. **Integridade Estrutural:** O payload é submetido a um parser validatório. Ausência de campos obrigatórios ou formatação inconsistente interrompe o fluxo (Early Error 400).
3. **Idempotência:** Consulta ao banco de dados SQLite (`transactions.db`) para atestar se o `transaction_id` já não consta na base local, evitando duplicidade de processamento.
4. **Validação de Negócio:** Checagem da veracidade matemática da transação, garantindo consistência estrita dos valores combinados de transação (`Amount` e `Currency`). Divergências engatilham a suspensão estrutural e acionam o endpoint de `/cancelar` no gateway emissor.
5. **Persistência e Confirmação:** Transações atestadas como íntegras são gravadas localmente de forma síncrona para garantir durabilidade no disco. Imediatamente após a inserção no SQLite, um request assíncrono é disparado para a rota `/confirmar` do gateway.

## Testes e Validação

O projeto conta com um script em Python que atua tanto como o orquestrador do envio dos webhooks quanto como o servidor (mock do gateway na porta 5001) para recepção das rotas de callback geradas por aprovações ou declínios.

> **OBS.:**: O script `test_webhook.py` foi adaptado do script forncecido pelo professor com ajustes para  manter a consistência da tipagem forte e semântica do payload. Dessa forma o campo `amount` que era enviado como string (`'49.9'`) foi convertido para um número decimal (`49.9`) para refletir a estrutura de dados definida no modelo F# e evitar erros de parsing., 

Para iniciar a bateria de validação, inicie o servidor da API F# em um terminal. Em um segundo terminal, execute:

```bash
python test_webhook.py
```

**Cenários avaliados pela execução:**

* Rejeição estrutural: Filtragem correta de token inválido, payload vazio e atributos suprimidos.
* Fluxo de aceite: Validação de transação real refletindo a resposta na porta do gateway.
* Bloqueio tático: Identificação de evento duplicado e captura de montantes e divergência de valores monetários.

> Como as transações são persistidas em banco, rodar os testes múltiplas vezes pode gerar rejeição por duplicidade. Para resetar o ambiente, basta excluir o arquivo `transactions.db` e reiniciar o servidor.