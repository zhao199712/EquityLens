using EquityLens.Api.Data.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace EquityLens.Api.Data.Configurations;

public sealed class AgentLlmTurnConfiguration : IEntityTypeConfiguration<AgentLlmTurn>
{
    public void Configure(EntityTypeBuilder<AgentLlmTurn> b)
    {
        b.ToTable("agent_llm_turn");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ActionJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.AgentRunNodeId, x.ExecutionAttempt, x.Turn }).IsUnique();
        b.HasOne<AgentRun>().WithMany().HasForeignKey(x => x.AgentRunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AgentRunNode>().WithMany().HasForeignKey(x => x.AgentRunNodeId).OnDelete(DeleteBehavior.Cascade);
        foreach (var property in b.Metadata.GetProperties())
            property.SetColumnName(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
    }
}

public sealed class AgentLoopCheckpointConfiguration : IEntityTypeConfiguration<AgentLoopCheckpoint>
{
    public void Configure(EntityTypeBuilder<AgentLoopCheckpoint> b)
    {
        b.ToTable("agent_loop_checkpoint");
        b.HasKey(x => x.Id);
        b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.StateJson).HasColumnType("jsonb");
        b.HasIndex(x => new { x.AgentRunNodeId, x.ExecutionAttempt }).IsUnique();
        b.HasOne<AgentRun>().WithMany().HasForeignKey(x => x.AgentRunId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AgentRunNode>().WithMany().HasForeignKey(x => x.AgentRunNodeId).OnDelete(DeleteBehavior.Cascade);
        foreach (var property in b.Metadata.GetProperties())
            property.SetColumnName(System.Text.Json.JsonNamingPolicy.SnakeCaseLower.ConvertName(property.Name));
    }
}
