open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting
open Giraffe
open Npgsql.FSharp
open Microsoft.AspNetCore.Http

[<CLIMutable>]
type Todo =
    { Id: Guid
      Text: string
      IsChecked: bool
      CreatedAt: DateTime
      UpdatedAt: DateTime }

[<CLIMutable>]
type NewTodo = { Text: string }

let TodoNotFound = $"Todo with id {id} could not be found"

let connectionString =
    Sql.host "localhost"
    |> Sql.database "postgres"
    |> Sql.username "postgres"
    |> Sql.password "mysecretpassword"
    |> Sql.port 5432
    |> Sql.formatConnectionString

let mapTodo (read: RowReader) : Todo =
    { Id = read.uuid "id"
      Text = read.string "text"
      IsChecked = read.bool "isChecked"
      CreatedAt = read.dateTime "createdAt"
      UpdatedAt = read.dateTime "updatedAt" }

let getTodoList () : Todo list =
    connectionString
    |> Sql.connect
    |> Sql.query "SELECT * FROM todos;"
    |> Sql.execute (mapTodo)

let insertTodo newTodo : Todo =
    connectionString
    |> Sql.connect
    |> Sql.query "INSERT INTO todos (text) VALUES (@text) RETURNING *;"
    |> Sql.parameters [ ("text", Sql.string newTodo.Text) ]
    |> Sql.executeRow (mapTodo)

let getTodoById id : Result<Todo, string> =
    let todos =
        connectionString
        |> Sql.connect
        |> Sql.query "SELECT * FROM todos where id = @id"
        |> Sql.parameters [ ("id", Sql.uuid id) ]
        |> Sql.execute (mapTodo)

    match todos with
    | [] -> Error TodoNotFound
    | _ -> Ok todos[0]

let deleteTodo id : Result<unit, string> =
    let rows =
        connectionString
        |> Sql.connect
        |> Sql.query "DELETE FROM todos WHERE id = @id"
        |> Sql.parameters [ ("id", Sql.uuid id) ]
        |> Sql.executeNonQuery

    match rows with
    | 0 -> Error TodoNotFound
    | _ -> Ok()

let checkTodo id : Result<unit, string> =
    let rows =
        connectionString
        |> Sql.connect
        |> Sql.query "UPDATE todos SET \"isChecked\" = true WHERE id = @id"
        |> Sql.parameters [ ("id", Sql.uuid id) ]
        |> Sql.executeNonQuery

    match rows with
    | 0 -> Error TodoNotFound
    | _ -> Ok()

let uncheckTodo id : Result<unit, string> =
    let rows =
        connectionString
        |> Sql.connect
        |> Sql.query "UPDATE todos SET \"isChecked\" = false WHERE id = @id"
        |> Sql.parameters [ ("id", Sql.uuid id) ]
        |> Sql.executeNonQuery

    match rows with
    | 0 -> Error TodoNotFound
    | _ -> Ok()

let handleGetTodos: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let todoList = getTodoList ()
        json todoList next ctx

let handleGetTodoById id : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let dbResult = getTodoById id

        match dbResult with
        | Error msg ->
            ctx.SetStatusCode 404
            text msg next ctx
        | Ok todo -> json todo next ctx

// TODO add validation, empty request bodies lead to todos created without
// name currently
let handlePostTodo: HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        task {
            let! newTodo = ctx.BindJsonAsync<NewTodo>()
            let todo = insertTodo newTodo
            ctx.SetStatusCode 201
            ctx.SetHttpHeader("Location", $"{ctx.GetRequestUrl()}/{todo.Id}")
            return! json todo next ctx
        }

let handleDeleteTodo id : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let dbResult = deleteTodo id

        match dbResult with
        | Error msg ->
            ctx.SetStatusCode 404
            text msg next ctx
        | Ok _ ->
            ctx.SetStatusCode 204
            next ctx

let handlePostTodoCheck id : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let dbResult = checkTodo id

        match dbResult with
        | Error msg ->
            ctx.SetStatusCode 404
            text msg next ctx
        | Ok _ ->
            ctx.SetStatusCode 204
            next ctx

let handleDeleteTodoCheck id : HttpHandler =
    fun (next: HttpFunc) (ctx: HttpContext) ->
        let dbResult = uncheckTodo id

        match dbResult with
        | Error msg ->
            ctx.SetStatusCode 404
            text msg next ctx
        | Ok _ ->
            ctx.SetStatusCode 204
            next ctx


let webApp =
    choose [ route "/todos"
             >=> choose [ GET >=> handleGetTodos
                          POST >=> handlePostTodo ]
             routef "/todos/%O" (fun id ->
                 (choose [ GET >=> handleGetTodoById id
                           DELETE >=> handleDeleteTodo id ]))
             routef "/todos/%O/check" (fun id ->
                 (choose [ POST >=> handlePostTodoCheck id
                           DELETE >=> handleDeleteTodoCheck id ])) ]

let builder = WebApplication.CreateBuilder()
builder.Services.AddGiraffe() |> ignore
let app = builder.Build()
app.UseGiraffe webApp
app.Run()
