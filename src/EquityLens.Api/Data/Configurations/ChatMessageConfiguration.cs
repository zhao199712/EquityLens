using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using EquityLens.Api.Data.Entities;

namespace EquityLens.Api.Data.Configurations;

public class ChatMessageConfiguration : IEntityTypeConfiguration<ChatMessage>
{
    public void Configure(EntityTypeBuilder<ChatMessage> builder)
    {
        builder.ToTable("chat_message");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(x => x.ChatSessionId).HasColumnName("chat_session_id").IsRequired();
        builder.Property(x => x.Role).HasColumnName("role").HasMaxLength(16).IsRequired();
        builder.Property(x => x.Content).HasColumnName("content");
        builder.Property(x => x.ToolName).HasColumnName("tool_name").HasMaxLength(64);
        builder.Property(x => x.ToolCallId).HasColumnName("tool_call_id").HasMaxLength(128);
        builder.Property(x => x.ToolCallsJson).HasColumnName("tool_calls_json");
        builder.Property(x => x.SequenceNumber).HasColumnName("sequence_number");
        builder.Property(x => x.CreatedAtUtc).HasColumnName("created_at_utc").HasDefaultValueSql("now()");

        builder.HasOne(x => x.ChatSession)
            .WithMany(s => s.Messages)
            .HasForeignKey(x => x.ChatSessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.ChatSessionId, x.SequenceNumber });
    }
}
