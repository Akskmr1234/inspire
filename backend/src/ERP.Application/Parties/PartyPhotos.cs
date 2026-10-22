using ERP.Application.Abstractions.Messaging;
using ERP.Application.Abstractions.Persistence;
using ERP.Application.Abstractions.Tenancy;
using ERP.Domain.Accounting;
using ERP.Domain.Parties;
using ERP.SharedKernel.Results;

namespace ERP.Application.Parties;

/// <summary>Reads a customer's or supplier's photograph.</summary>
/// <param name="LedgerId">The party's ledger.</param>
public sealed record GetPartyPhotoQuery(Guid LedgerId) : IQuery<PartyPhotoResponse>;

/// <summary>A party's photograph.</summary>
/// <param name="Content">The picture as a data URL, or null when there is none.</param>
public sealed record PartyPhotoResponse(string? Content);

/// <summary>Sets, replaces or removes one of a customer's, supplier's or employee's photographs.</summary>
/// <param name="LedgerId">The party's ledger.</param>
/// <param name="Content">The picture as a data URL; null removes it.</param>
/// <param name="Slot">Which photograph, from one; the first is the one a card shows.</param>
public sealed record SetPartyPhotoCommand(Guid LedgerId, string? Content, int Slot = 1) : ICommand;

/// <summary>Reads every photograph a party has.</summary>
/// <param name="LedgerId">The party's ledger.</param>
public sealed record ListPartyPhotosQuery(Guid LedgerId) : IQuery<PhotoSetResponse>;

/// <summary>A record's photographs, one entry a slot.</summary>
/// <param name="Photos">The pictures as data URLs, in slot order; null where a slot is empty.</param>
public sealed record PhotoSetResponse(IReadOnlyList<string?> Photos);

/// <summary>Handles party photographs.</summary>
public sealed class PartyPhotoHandlers
    : IQueryHandler<GetPartyPhotoQuery, PartyPhotoResponse>,
      IQueryHandler<ListPartyPhotosQuery, PhotoSetResponse>,
      ICommandHandler<SetPartyPhotoCommand>
{
    private readonly ILedgerRepository _ledgers;
    private readonly IPlaceRepository _places;
    private readonly ITenantContext _tenantContext;
    private readonly IUnitOfWork _unitOfWork;

    /// <summary>Initialises a new instance of the <see cref="PartyPhotoHandlers"/> class.</summary>
    /// <param name="ledgers">The ledger repository.</param>
    /// <param name="places">Where the photographs are kept.</param>
    /// <param name="tenantContext">The ambient tenant scope.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    public PartyPhotoHandlers(
        ILedgerRepository ledgers,
        IPlaceRepository places,
        ITenantContext tenantContext,
        IUnitOfWork unitOfWork)
    {
        _ledgers = ledgers;
        _places = places;
        _tenantContext = tenantContext;
        _unitOfWork = unitOfWork;
    }

    /// <inheritdoc />
    public async Task<Result<PartyPhotoResponse>> Handle(
        GetPartyPhotoQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Ledger> party = await FindPartyAsync(request.LedgerId, cancellationToken);

        if (party.IsFailure)
        {
            return Result.Failure<PartyPhotoResponse>(party.Error);
        }

        PartyPhoto? photo = await _places.FindPhotoAsync(
            party.Value.Id, cancellationToken: cancellationToken);

        return Result.Success(new PartyPhotoResponse(photo?.Content));
    }

    /// <inheritdoc />
    public async Task<Result<PhotoSetResponse>> Handle(
        ListPartyPhotosQuery request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Ledger> party = await FindPartyAsync(request.LedgerId, cancellationToken);

        if (party.IsFailure)
        {
            return Result.Failure<PhotoSetResponse>(party.Error);
        }

        IReadOnlyList<PartyPhoto> photos = await _places.ListPhotosAsync(party.Value.Id, cancellationToken);

        return Result.Success(new PhotoSetResponse(
        [
            .. Enumerable.Range(1, PartyPhoto.MaximumSlots)
                .Select(slot => photos.FirstOrDefault(photo => photo.Slot == slot)?.Content),
        ]));
    }

    /// <inheritdoc />
    public async Task<Result> Handle(SetPartyPhotoCommand request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        Result<Ledger> party = await FindPartyAsync(request.LedgerId, cancellationToken);

        if (party.IsFailure)
        {
            return party;
        }

        PartyPhoto? photo = await _places.FindPhotoAsync(
            party.Value.Id, request.Slot, cancellationToken);

        if (request.Content is null)
        {
            if (photo is not null)
            {
                _places.RemovePhoto(photo);
            }
        }
        else if (photo is not null)
        {
            Result replaced = photo.Replace(request.Content);

            if (replaced.IsFailure)
            {
                return replaced;
            }
        }
        else
        {
            Result<PartyPhoto> created = PartyPhoto.Create(
                party.Value.TenantId, party.Value.FirmId, party.Value.Id, request.Content, request.Slot);

            if (created.IsFailure)
            {
                return created;
            }

            _places.AddPhoto(created.Value);
        }

        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Result.Success();
    }

    /// <summary>Finds a customer or supplier of the selected firm.</summary>
    private async Task<Result<Ledger>> FindPartyAsync(Guid ledgerId, CancellationToken cancellationToken)
    {
        Ledger? ledger = await _ledgers.FindAsync(LedgerId.From(ledgerId), cancellationToken);

        return ledger is null
            || ledger.FirmId != _tenantContext.FirmId
            || ledger.Kind is not (LedgerKind.Customer or LedgerKind.Supplier or LedgerKind.Employee)
            ? Result.Failure<Ledger>(Error.NotFound(
                "PartyPhoto.PartyNotFound", "No such customer or supplier in the selected firm."))
            : Result.Success(ledger);
    }
}
