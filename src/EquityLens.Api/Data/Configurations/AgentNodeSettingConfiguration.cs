using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class AgentNodeSettingConfiguration : IEntityTypeConfiguration<AgentNodeSetting>
{
    public void Configure(EntityTypeBuilder<AgentNodeSetting> b)
    {
        b.ToTable("agent_node_setting"); b.HasKey(x => x.Id);
        b.Property(x => x.NodeType).HasMaxLength(128).IsRequired(); b.HasIndex(x => x.NodeType).IsUnique();
        b.Property(x => x.DisplayName).HasMaxLength(160); b.Property(x => x.Description).HasMaxLength(2000);
        b.Property(x => x.MetadataJson).HasColumnType("jsonb");
    }
}
