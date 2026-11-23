using MediatR;

namespace Reviews.Application.Common.Messaging;

/// <summary>Команда без результату (зміна стану).</summary>
public interface ICommand : IRequest;

/// <summary>Команда з результатом (наприклад, створений ресурс).</summary>
public interface ICommand<out TResponse> : IRequest<TResponse>;

/// <summary>Запит на читання. Не змінює стан, читає через інтерфейси *Queries.</summary>
public interface IQuery<out TResponse> : IRequest<TResponse>;
