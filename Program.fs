open System
open Microsoft.AspNetCore.Builder
open Microsoft.Extensions.Hosting
open Giraffe
open Npgsql
open Npgsql.FSharp
open Microsoft.Extensions.DependencyInjection

[<CLIMutable>]
type Todo = {
    Id: Guid
    Text: string
    IsChecked: bool
    CreatedAt: DateTime
    UpdatedAt: DateTime
}

let connectionString = 
    Sql.host "localhost"
    |> Sql.database "postgres"
    |> Sql.username "postgres"
    |> Sql.password "mysecretpassword"
    |> Sql.port 5432
    |> Sql.formatConnectionString

let getTodoList : Todo list =
    connectionString
    |> Sql.connect
    |> Sql.query "SELECT * FROM todos;"
    |> Sql.execute (fun read -> 
        {
            Id = read.uuid "id"
            Text = read.string "text"
            IsChecked = read.bool "isChecked"
            CreatedAt = read.dateTime "createdAt"
            UpdatedAt = read.dateTime "updatedAt"
        })

let webApp =
    choose [ route "/todos"
             >=> choose [ GET >=> setStatusCode 200 >=> json getTodoList
                          POST >=> text "POST /todos " ]
             routef "/todos/%s" (fun id ->
                 (choose [ GET >=> text "GET /todos/{id}"
                           DELETE >=> text "DELETE /todos/{id}" ]))
             routef "/todos/%s/check" (fun id ->
                 (choose [ POST >=> text "POST /todos/{id}/check"
                           DELETE >=> text "DELETE /todos/{id}/check" ])) ]


let builder = WebApplication.CreateBuilder()
builder.Services.AddGiraffe() |> ignore
let app = builder.Build()
app.UseGiraffe webApp
app.Run()
