using MongoDB.Bson;
using Reviews.Domain.Entities;

namespace Reviews.Domain.Interfaces;

/// <summary>
/// Репозиторій запису відгуків. Реалізація — в Infrastructure (MongoDB Driver).
/// Оновлення й видалення — з перевіркою версії (оптимістична конкурентність):
/// якщо документ змінив хтось інший, кидається <see cref="Exceptions.ConflictException"/>.
/// </summary>
public interface IReviewRepository
{
    Task<Review?> GetByIdAsync(ObjectId id, CancellationToken ct = default);

    /// <summary>Чи залишав уже користувач відгук на цей фільм (правило «один відгук на фільм»).</summary>
    Task<bool> ExistsAsync(int movieId, string userId, CancellationToken ct = default);

    Task AddAsync(Review review, CancellationToken ct = default);

    /// <summary>Замінює документ, лише якщо в базі все ще очікувана версія (review.Version до виклику).</summary>
    Task UpdateAsync(Review review, CancellationToken ct = default);

    Task DeleteAsync(Review review, CancellationToken ct = default);

    /// <summary>Масове оновлення денормалізованої назви фільму (обробник події MovieRenamed з Каталогу).</summary>
    Task<long> RenameMovieAsync(int movieId, string movieTitle, CancellationToken ct = default);
}

public interface IDiscussionRepository
{
    Task<Discussion?> GetByIdAsync(ObjectId id, CancellationToken ct = default);
    Task AddAsync(Discussion discussion, CancellationToken ct = default);
    Task UpdateAsync(Discussion discussion, CancellationToken ct = default);

    /// <summary>Лаб. 08: масове оновлення денормалізованої назви фільму в обговореннях (подія MovieRenamed).</summary>
    Task<long> RenameMovieAsync(int movieId, string movieTitle, CancellationToken ct = default);
}
