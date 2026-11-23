using AutoMapper;
using Reviews.Application.Discussions.Commands;
using Reviews.Application.Reviews.Commands;

namespace Reviews.WebApi.Contracts;

/// <summary>HTTP-контракти → команди MediatR (ідентифікатори з маршруту додаються через контекст мапінгу).</summary>
public sealed class RequestMappingProfile : Profile
{
    public const string IdKey = "id";
    public const string VersionKey = "version";

    public RequestMappingProfile()
    {
        AllowNullCollections = true;                    // відсутні aspects лишаються null, а не порожнім словником

        CreateMap<UpdateReviewRequest, UpdateReviewCommand>()
            .ForCtorParam(nameof(UpdateReviewCommand.Id), o => o.MapFrom((_, ctx) => (string)ctx.Items[IdKey]))
            .ForCtorParam(nameof(UpdateReviewCommand.ExpectedVersion),
                o => o.MapFrom((src, ctx) => ctx.Items.TryGetValue(VersionKey, out var v) && v is int header ? header : src.Version));

        CreateMap<AddCommentRequest, AddCommentCommand>()
            .ForCtorParam(nameof(AddCommentCommand.ReviewId), o => o.MapFrom((_, ctx) => (string)ctx.Items[IdKey]));

        CreateMap<CloseDiscussionRequest, CloseDiscussionCommand>()
            .ForCtorParam(nameof(CloseDiscussionCommand.Id), o => o.MapFrom((_, ctx) => (string)ctx.Items[IdKey]))
            .ForCtorParam(nameof(CloseDiscussionCommand.ExpectedVersion),
                o => o.MapFrom((src, ctx) => ctx.Items.TryGetValue(VersionKey, out var v) && v is int header ? header : src.Version));
    }
}
