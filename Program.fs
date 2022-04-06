open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting
open Giraffe
open Npgsql.FSharp

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

let getTodoList: Todo list =
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
        |> Sql.parameters [("id", Sql.uuid id)]
        |> Sql.executeNonQuery
    
    match rows with
    | 0 -> Error TodoNotFound
    | _ -> Ok ()

let handleGetTodos = 
    setStatusCode 200 
    >=> json getTodoList 

let handleGetTodoById id =
    let dbResult = getTodoById id
    match dbResult with
    | Error msg -> setStatusCode 404 >=> text msg
    | Ok todo -> json todo

let handlePostTodo =
    setStatusCode 201
    >=> bindJson<NewTodo> (fun newTodo -> json (insertTodo newTodo))

let handleDeleteTodo id =
    let dbResult = deleteTodo id
    match dbResult with
    | Error msg -> setStatusCode 404 >=> text msg
    | Ok _ -> setStatusCode 204

let webApp =
    choose [ route "/todos"
             >=> choose [ GET >=> handleGetTodos
                          POST >=> handlePostTodo ]
             routef "/todos/%O" (fun id ->
                 (choose [ GET >=> handleGetTodoById id
                           DELETE >=> handleDeleteTodo id ]))
             routef "/todos/%s/check" (fun id ->
                 (choose [ POST >=> text "POST /todos/{id}/check"
                           DELETE >=> text "DELETE /todos/{id}/check" ])) ]

let builder = WebApplication.CreateBuilder()
builder.Services.AddGiraffe() |> ignore
let app = builder.Build()
app.UseGiraffe webApp
app.Run()
