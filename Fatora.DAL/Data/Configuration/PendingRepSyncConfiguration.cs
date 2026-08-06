using Fatora.DAL.Entites;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Fatora.DAL.Data.Configuration;

public class PendingRepSyncConfiguration : IEntityTypeConfiguration<PendingRepSync>
{
    public void Configure(EntityTypeBuilder<PendingRepSync> builder)
    {
        builder.HasIndex(x => x.RepId).IsUnique();

        builder.HasOne(x => x.Rep).WithMany().HasForeignKey(x => x.RepId).OnDelete(DeleteBehavior.Cascade);
    }
}
