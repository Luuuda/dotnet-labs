using AutoMapper;
using MongoDB.Bson;
using Reviews.Application.Common.Models;
using Reviews.Domain.Entities;
using Reviews.Domain.Enums;
using Reviews.Domain.ValueObjects;

namespace Reviews.Application.Common.Mapping;

/// <summary>
/// Мапінг доменних сутностей у DTO: ObjectId → string, Value Objects (Author, Rating, ReviewAspects)
/// → прості значення, вкладені документи й масиви (comments) → списки DTO.
/// </summary>
public sealed class MappingProfile : Profile
{
    public MappingProfile()
    {
        CreateMap<ObjectId, string>().ConvertUsing(id => id.ToString());
        CreateMap<Rating, int>().ConvertUsing(r => r.Value);
        CreateMap<ReviewAspects?, IReadOnlyDictionary<string, int>?>()
            .ConvertUsing(a => a == null ? null : new Dictionary<string, int>(a.Scores));
        CreateMap<DiscussionStatus, string>().ConvertUsing(s => s.ToString().ToLowerInvariant());

        CreateMap<Author, AuthorDto>();
        CreateMap<Comment, CommentDto>();

        CreateMap<Review, ReviewDto>();
        CreateMap<Review, ReviewDetailsDto>()
            .ForMember(d => d.Comments, o => o.MapFrom(s => s.Comments.OrderBy(c => c.CreatedAt)));

        CreateMap<Discussion, DiscussionDto>();
    }
}
