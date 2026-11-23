# Лабораторні роботи: розробка додатків на платформі .NET

Андрющенко Людмила, група 443-2

Кожна лабораторна робота лежить в окремій гілці, відгалуженій від `main`:

| Гілка | Лабораторна | Тема |
|---|---|---|
| `lab-01` | Lab01 | Проєктування баз даних для мікросервісної архітектури (предметна область «Кінотеатр») |
| `lab-02` | Lab02 | Тришарова архітектура на ADO.NET та Dapper — мікросервіс бронювання |
| `lab-03` | Lab03 | Тришарова архітектура на Entity Framework Core — мікросервіс каталогу фільмів |
| `lab-04` | Lab04 | Clean Architecture, CQRS (MediatR) і MongoDB — мікросервіс відгуків |

## Lab04 (ця гілка)

Усе в папці [`Lab04-ReviewsService/`](Lab04-ReviewsService/): чотири шари Clean Architecture (Domain ← Application ← Infrastructure, WebApi), команди й запити MediatR з Pipeline Behaviors, MongoDB з BsonClassMap, індексами й сідінгом. Інструкція — [`Lab04-ReviewsService/README.md`](Lab04-ReviewsService/README.md).
