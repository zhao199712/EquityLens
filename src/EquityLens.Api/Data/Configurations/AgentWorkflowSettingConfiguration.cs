using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class AgentWorkflowSettingConfiguration : IEntityTypeConfiguration<AgentWorkflowSetting>
{
    public void Configure(EntityTypeBuilder<AgentWorkflowSetting> b)
    {
        b.ToTable("agent_workflow_setting"); b.HasKey(x => x.Id);
        b.Property(x => x.WorkflowType).HasMaxLength(64).IsRequired(); b.HasIndex(x => x.WorkflowType).IsUnique();
        b.Property(x => x.DisplayName).HasMaxLength(160); b.Property(x => x.Description).HasMaxLength(2000);
    }
}
