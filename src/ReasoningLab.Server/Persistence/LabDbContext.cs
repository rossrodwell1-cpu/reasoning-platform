using Microsoft.EntityFrameworkCore;

namespace ReasoningLab.Server.Persistence;

public class LabDbContext(DbContextOptions<LabDbContext> options) : DbContext(options)
{
    public DbSet<Skill> Skills => Set<Skill>();
    public DbSet<SkillPrerequisite> Prerequisites => Set<SkillPrerequisite>();
    public DbSet<Pathway> Pathways => Set<Pathway>();
    public DbSet<PathwaySkill> PathwaySkills => Set<PathwaySkill>();
    public DbSet<ExamModule> Modules => Set<ExamModule>();
    public DbSet<ExamBlueprint> Blueprints => Set<ExamBlueprint>();
    public DbSet<QuestionBank> Banks => Set<QuestionBank>();
    public DbSet<QuestionVersion> Questions => Set<QuestionVersion>();
    public DbSet<Learner> Learners => Set<Learner>();
    public DbSet<LearningSession> Sessions => Set<LearningSession>();
    public DbSet<SessionItem> Items => Set<SessionItem>();
    public DbSet<Attempt> Attempts => Set<Attempt>();
    public DbSet<Mastery> Mastery => Set<Mastery>();
    public DbSet<MasteryEvidence> Evidence => Set<MasteryEvidence>();
    public DbSet<Misconception> Misconceptions => Set<Misconception>();
    public DbSet<DiagnosticResult> Results => Set<DiagnosticResult>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<SkillPrerequisite>().HasKey(x => new { x.SkillId, x.PrerequisiteId });
        b.Entity<SkillPrerequisite>().HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SkillPrerequisite>().HasOne<Skill>().WithMany().HasForeignKey(x => x.PrerequisiteId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<PathwaySkill>().HasKey(x => new { x.PathwayId, x.SkillId });
        b.Entity<PathwaySkill>().HasOne<Pathway>().WithMany().HasForeignKey(x => x.PathwayId);
        b.Entity<PathwaySkill>().HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId);
        b.Entity<ExamBlueprint>().HasOne<ExamModule>().WithMany().HasForeignKey(x => x.ModuleId);
        b.Entity<QuestionVersion>().HasIndex(x => new { x.Key, x.Version }).IsUnique();
        b.Entity<QuestionVersion>().HasOne<QuestionBank>().WithMany().HasForeignKey(x => x.BankId);
        b.Entity<QuestionVersion>().HasOne<Pathway>().WithMany().HasForeignKey(x => x.PathwayId);
        b.Entity<QuestionVersion>().HasMany(x => x.Skills).WithOne().HasForeignKey(x => x.QuestionVersionId);
        b.Entity<QuestionVersion>().ToTable(t => t.HasCheckConstraint("positive_difficulty", "\"Difficulty\" > 0"));
        b.Entity<QuestionSkill>().HasKey(x => new { x.QuestionVersionId, x.SkillId });
        b.Entity<QuestionSkill>().HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId);
        b.Entity<LearningSession>().HasOne<Learner>().WithMany().HasForeignKey(x => x.LearnerId);
        b.Entity<LearningSession>().HasOne<Pathway>().WithMany().HasForeignKey(x => x.PathwayId);
        b.Entity<LearningSession>().HasOne<ExamBlueprint>().WithMany().HasForeignKey(x => x.BlueprintId);
        b.Entity<LearningSession>().HasMany(x => x.Items).WithOne().HasForeignKey(x => x.SessionId);
        b.Entity<SessionItem>().HasOne(x => x.Question).WithMany().HasForeignKey(x => x.QuestionVersionId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<SessionItem>().HasIndex(x => new { x.SessionId, x.Position }).IsUnique();
        b.Entity<SessionItem>().HasOne(x => x.Attempt).WithOne().HasForeignKey<Attempt>(x => x.SessionItemId);
        b.Entity<Mastery>().HasKey(x => new { x.LearnerId, x.SkillId });
        b.Entity<Mastery>().HasOne<Learner>().WithMany().HasForeignKey(x => x.LearnerId);
        b.Entity<Mastery>().HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId);
        b.Entity<MasteryEvidence>().HasKey(x => new { x.AttemptId, x.SkillId });
        b.Entity<MasteryEvidence>().HasOne<Attempt>().WithMany().HasForeignKey(x => x.AttemptId);
        b.Entity<MasteryEvidence>().HasOne<Skill>().WithMany().HasForeignKey(x => x.SkillId);
        b.Entity<MasteryEvidence>().HasOne<Learner>().WithMany().HasForeignKey(x => x.LearnerId);
        b.Entity<Misconception>().HasKey(x => new { x.LearnerId, x.Tag });
        b.Entity<Misconception>().HasOne<Learner>().WithMany().HasForeignKey(x => x.LearnerId);
        b.Entity<DiagnosticResult>().HasKey(x => x.SessionId);
        b.Entity<DiagnosticResult>().HasOne<LearningSession>().WithOne().HasForeignKey<DiagnosticResult>(x => x.SessionId);
    }
}
