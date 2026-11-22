# Лабораторні роботи: розробка додатків на платформі .NET

Андрющенко Людмила, група 443-2

Кожна лабораторна робота лежить в окремій гілці, відгалуженій від `main`:

| Гілка | Лабораторна | Тема |
|---|---|---|
| `lab-01` | Lab01 | Проєктування баз даних для мікросервісної архітектури (предметна область «Кінотеатр») |
| `lab-02` | Lab02 | Тришарова архітектура на ADO.NET та Dapper — мікросервіс бронювання |
| `lab-03` | Lab03 | Тришарова архітектура на Entity Framework Core — мікросервіс каталогу фільмів |

## Lab03 (ця гілка)

Усе в папці [`Lab03-CatalogService/`](Lab03-CatalogService/): EF Core Code-First (міграції, Fluent API, сідінг) → Generic Repository + Unit of Work → BLL (AutoMapper, FluentValidation, специфікації) → Web API з пагінацією, ProblemDetails і Serilog. Інструкція — [`Lab03-CatalogService/README.md`](Lab03-CatalogService/README.md).
