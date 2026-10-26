using ERP.Domain.Numbering;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace ERP.Infrastructure.Persistence.Conversion;

/// <summary>Converts <see cref="NumberingSeriesId"/> to and from <see cref="Guid"/>.</summary>
public sealed class NumberingSeriesIdConverter : ValueConverter<NumberingSeriesId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="NumberingSeriesIdConverter"/> class.</summary>
    public NumberingSeriesIdConverter()
        : base(id => id.Value, value => NumberingSeriesId.From(value))
    {
    }
}

/// <summary>Converts <see cref="MasterCodeSequenceId"/> to and from <see cref="Guid"/>.</summary>
public sealed class MasterCodeSequenceIdConverter : ValueConverter<MasterCodeSequenceId, Guid>
{
    /// <summary>Initialises a new instance of the <see cref="MasterCodeSequenceIdConverter"/> class.</summary>
    public MasterCodeSequenceIdConverter()
        : base(id => id.Value, value => MasterCodeSequenceId.From(value))
    {
    }
}
