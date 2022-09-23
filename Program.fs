module TodoExample

open System
open System.Data
open Falco
open Falco.HostBuilder
open Falco.Routing
open Npgsql
open Dapper
open Microsoft.Extensions.Configuration
open Microsoft.Extensions.Logging

module Service =
    let connectionString =     
                ConfigurationBuilder()
                    .AddJsonFile("appsettings.json")
                    .AddJsonFile("appsettings.Development.json")
                    .Build()
                    .GetConnectionString("Default") 

    let connectionFactory () = new NpgsqlConnection(connectionString) :> IDbConnection

    let run 
        (validate: 'input -> Result<'b, 'error>)
        (provider: IDbConnection -> 'b -> Result<'c, 'error>)
        (handleOk: ILogger -> 'c -> HttpHandler)
        (handleError: ILogger -> 'input -> 'error -> HttpHandler)
        (input: 'input): HttpHandler = 
        fun ctx ->
            use conn = connectionFactory ()
            let log = ctx.GetLogger "Todos"

            let respondWith = 
                match (validate >> Result.bind (provider conn)) input with
                | Ok output -> handleOk log output
                | Error error -> handleError log input error 

            respondWith ctx

module Result =
    let created (obj: 'a): HttpHandler =
        Response.withStatusCode 201 >>
        Response.ofJson obj

    let badRequest: HttpHandler =
        Response.withStatusCode 400 >>
        Response.ofPlainText "Bad request"

    let notFound: HttpHandler =
        Response.withStatusCode 404 >>
        Response.ofPlainText "Not found"

    let noContent: HttpHandler =
        Response.withStatusCode 204 >>
        Response.ofEmpty

module Todos = 
    type Todo = 
        { Id: Guid
        ; Text: string
        ; IsChecked: bool
        ; CreatedAt: DateTime
        ; UpdatedAt: DateTime }

    type Error =
        | InvalidText 
        | InvalidId
        | TodoNotFound

    let handleError (log: ILogger) input error = 
                match error with
                | InvalidText | InvalidId ->
                    log.LogError $"Invalid input {input} has been submitted"
                    Result.badRequest
                | TodoNotFound -> 
                    log.LogInformation $"Todo with id {input} could not be found"
                    Result.notFound

    module New = 
        type newTodo = { Text: String }

        let mutation 
            (conn: IDbConnection)
            (newTodo: newTodo) = 
            let sql = "
                INSERT INTO todos (text) 
                VALUES (@Text) 
                RETURNING *;"

            let data = {| text = newTodo.Text |}
            Ok (conn.QueryFirst<Todo>(sql, data))

        let validate (newTodo: newTodo) =
            match newTodo.Text with
            | "" -> Error InvalidText
            | null -> Error InvalidText
            | _ -> Ok newTodo

        let handle =   
            let handleOk _ (todo: Todo): HttpHandler = 
                Result.created todo

            Request.mapJson (Service.run validate mutation handleOk handleError)

    module GetAll =               
        let query 
            (conn: IDbConnection)
            _ = 
            let sql = "SELECT * FROM todos;"
            Ok (conn.Query<Todo>(sql))

        let validate _ = Ok () 
        
        let handle =
            let handleOk _ todos = 
                Response.ofJson todos
            
            Service.run validate query handleOk handleError ()

    module Get =
        let query 
            (conn: IDbConnection)
            (id: Guid) = 
            let sql = "
                SELECT * FROM todos
                WHERE id = (@Id);"

            let data = {| id = id |}
            match box (conn.QueryFirstOrDefault<Todo>(sql, data)) with 
            | null -> Error TodoNotFound
            | todo -> Ok (unbox todo)

        let validate (idOption: Guid option) =
            match idOption with
            | Some id -> Ok id
            | None -> Error InvalidId

        let handle: HttpHandler =
            let routeMap (route : RouteCollectionReader) = 
                route.TryGetGuid "id"

            let handleOk _ (todo: Todo) =  
                todo |>
                Response.ofJson

            Request.mapRoute routeMap (Service.run validate query handleOk handleError)

    module Delete = 
        let mutation
            (conn: IDbConnection) 
            (id: Guid) = 
            let sql = "
                DELETE FROM todos
                WHERE id = (@Id);"

            let data = {| id = id |}
            let changed = conn.Execute(sql, data)

            match changed with 
            | 0 -> Error TodoNotFound
            | _ -> Ok ()

        let validate (idOption: Guid option) = 
            match idOption with
            | Some id -> Ok id
            | None -> Error InvalidId

        let handle: HttpHandler = 
            let routeMap (route : RouteCollectionReader) = 
                route.TryGetGuid "id"

            let handleOk _ () =
                Result.noContent
                
            Request.mapRoute routeMap (Service.run validate mutation handleOk handleError)
    
    module Check = 
        let mutation
            (conn: IDbConnection) 
            (id: Guid) = 
            let sql = "
                UPDATE todos
                SET \"isChecked\" = true
                WHERE id = (@Id);"

            let data = {| id = id |}
            let changed = conn.Execute(sql, data)

            match changed with 
            | 0 -> Error TodoNotFound
            | _ -> Ok ()

        let validate (idOption: Guid option) = 
            match idOption with
            | Some id -> Ok id
            | None -> Error InvalidId

        let handle: HttpHandler = 
            let routeMap (route : RouteCollectionReader) = 
                route.TryGetGuid "id"

            let handleOk _ () =
                Result.noContent
                
            Request.mapRoute routeMap (Service.run validate mutation handleOk handleError)

    module Uncheck = 
        let mutation
            (conn: IDbConnection) 
            (id: Guid) = 
            let sql = "
                UPDATE todos
                SET \"isChecked\" = false
                WHERE id = (@Id);"

            let data = {| id = id |}
            let changed = conn.Execute(sql, data)
            match changed with 
            | 0 -> Error TodoNotFound
            | _ -> Ok ()

        let validate (idOption: Guid option) = 
            match idOption with
            | Some id -> Ok id
            | None -> Error InvalidId

        let handle: HttpHandler = 
            let routeMap (route : RouteCollectionReader) = 
                route.TryGetGuid "id"

            let handleOk _ () =
                Result.noContent
                
            Request.mapRoute routeMap (Service.run validate mutation handleOk handleError)

module Program = 
    [<EntryPoint>]
    let main args =
        webHost args {
            endpoints [ 
                all "/todos" 
                    [ POST, Todos.New.handle
                    ; GET, Todos.GetAll.handle ]

                all "/todos/{id:guid}" 
                    [ GET, Todos.Get.handle
                    ; DELETE, Todos.Delete.handle ]

                all "/todos/{id:guid}/check" 
                    [ POST, Todos.Check.handle
                    ; DELETE, Todos.Uncheck.handle ] 
            ]
        }
        0