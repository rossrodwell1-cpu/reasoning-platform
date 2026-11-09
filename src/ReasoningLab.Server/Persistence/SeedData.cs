using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace ReasoningLab.Server.Persistence;

public record DiagnosticRules(string[] QuestionKeys, int Seconds, string[] Tools, string Score);

public static class SeedData
{
    public static async Task Initialize(LabDbContext db)
    {
        if (await db.Skills.AnyAsync()) return;
        await using var transaction = await db.Database.BeginTransactionAsync();
        (string Id, string Name)[] skills = [
            ("deduction", "Deductive reasoning"), ("induction", "Inductive reasoning"),
            ("conditional", "Conditional logic"), ("verbal", "Verbal reasoning & inference"),
            ("quantitative", "Quantitative reasoning"), ("critical", "Critical analysis"),
            ("decomposition", "Problem decomposition"), ("constraints", "Constraint satisfaction"),
            ("spatial", "Spatial & abstract reasoning"), ("lateral", "Lateral thinking & reframing"),
            ("uncertainty", "Decision-making under uncertainty")];
        db.Skills.AddRange(skills.Select(s => new Skill { Id = s.Id, Name = s.Name }));
        db.Prerequisites.Add(new() { SkillId = "conditional", PrerequisiteId = "deduction" });
        foreach (var (id, name, description) in new[] {
            ("general", "Everyday reasoning", "Build the habits behind clear thinking. Start small, connect ideas, then take on a harder challenge."),
            ("lsat", "LSAT preparation", "Bring your reasoning skills to arguments and conclusions. Original introductory practice."),
            ("ucat", "UCAT preparation", "Explore what follows from the evidence. Original introductory syllogism practice.") })
        {
            db.Pathways.Add(new() { Id = id, Name = name, Description = description });
            db.PathwaySkills.Add(new() { PathwayId = id, SkillId = "deduction", Position = 1 });
            db.Banks.Add(new() { Id = id, Name = name + " · original starter bank" });
            if (id != "general") db.Modules.Add(new() { Id = id, Name = name });
        }
        db.Blueprints.AddRange(
            new() { Id = "lsat-2024", ModuleId = "lsat", EffectiveFrom = new(2024, 8, 1), EffectiveUntil = new(2027, 8, 1), Status = "Reference only", Source = "https://www.lsac.org/lsat/prepare/types-lsat-questions", RulesJson = """{"sections":4,"secondsPerSection":2100,"scoredSections":["LR","LR","RC"],"unscoredSection":"LR or RC","verified":"2026-11-09"}""" },
            new() { Id = "lsat-2027", ModuleId = "lsat", EffectiveFrom = new(2027, 8, 1), Status = "Reference only", Source = "https://www.lsac.org/lsat-will-look-little-different-starting-august-2027", RulesJson = """{"sections":[{"type":"RC","seconds":3120,"questions":40,"scored":27},{"type":"LR","seconds":2700,"questions":32,"scored":25},{"type":"LR","seconds":2700,"questions":32,"scored":25}],"breaksSeconds":[600,600],"verified":"2026-11-09"}""" },
            new() { Id = "ucat-reference", ModuleId = "ucat", EffectiveFrom = new(2026, 1, 1), Status = "Full verification pending", Source = "https://www.ucat.ac.uk/about-ucat/test-format-and-scoring/test-timings/" },
            new() { Id = "lsat-diagnostic-v1", ModuleId = "lsat", EffectiveFrom = new(2026, 11, 9), Status = "Original short diagnostic", RulesJson = JsonSerializer.Serialize(new DiagnosticRules(["lsat-1", "lsat-2", "lsat-4"], 180, [], "Raw correctness only")) });

        Add(db, "general", 1, 1, "Every solar-powered lamp in the garden has a blue label. This lamp is solar-powered. What must be true?", ["It has a blue label.", "Every blue-labelled lamp is solar-powered.", "It is the only lamp in the garden."], 0, "The rule applies to every solar-powered lamp, including this one. The reverse rule was never stated.");
        Add(db, "general", 2, 1, "All members of the cycling club own a helmet. Noor is a member. Which conclusion follows?", ["Noor cycles every day.", "Noor owns a helmet.", "Everyone who owns a helmet is a member."], 1, "Membership guarantees helmet ownership. It tells us nothing about frequency or whether other helmet owners are members.");
        Add(db, "general", 3, 1, "No reference books may leave the library. The atlas is a reference book. What follows?", ["The atlas is never read.", "Only atlases are reference books.", "The atlas may not leave the library."], 2, "The restriction applies to all reference books, so it applies to the atlas.");
        Add(db, "general", 4, 2, "Every parcel marked urgent is sent by courier. No courier parcel travels by sea. Parcel K is urgent. Which statement must be true?", ["Parcel K does not travel by sea.", "All parcels that avoid the sea are urgent.", "Parcel K arrives today."], 0, "Chain the rules: urgent → courier → not by sea. Neither a reverse implication nor an arrival date follows.");
        Add(db, "general", 5, 2, "If the greenhouse heater runs, the indicator glows. The indicator is not glowing. Which conclusion is valid?", ["The heater is broken.", "The heater is not running.", "The greenhouse is cold."], 1, "If running always produces a glow, no glow rules out running. This is the contrapositive; the cause of the state remains unknown.");
        Add(db, "general", 6, 2, "All archive keys are numbered. No numbered key is kept on the red hook. A key is on the red hook. What follows?", ["It opens no door.", "It is an archive key.", "It is not an archive key."], 2, "An archive key would be numbered and therefore could not be on the red hook. Work backwards through both constraints.");
        Add(db, "general", 7, 3, "Every approved plan has either a safety review or an exemption. Exempt plans are never coastal. Plan P is coastal and approved. What must it have?", ["An exemption.", "A safety review.", "Both an exemption and a safety review."], 1, "Coastal rules out an exemption. Since an approved plan needs at least one of the two, P must have a safety review.");
        Add(db, "lsat", 1, 1, "A council rule states that every licensed street vendor has completed an inspection. Mara is a licensed street vendor. Which conclusion follows from these statements?", ["Mara has completed an inspection.", "Every inspected vendor is licensed.", "Mara passed more than one inspection."], 0, "The stated condition guarantees an inspection for Mara. Inspection alone does not guarantee a licence.");
        Add(db, "lsat", 2, 1, "All reports accepted by a journal receive editorial review. A report was accepted. Which inference is supported?", ["The report is correct in every detail.", "The report received editorial review.", "All reviewed reports are accepted."], 1, "Acceptance entails review, but review is not a guarantee of correctness or acceptance.");
        Add(db, "lsat", 3, 1, "No confidential document is available in the public archive. File R is confidential. What follows?", ["File R has been destroyed.", "Every file outside the archive is confidential.", "File R is not available in the public archive."], 2, "The exclusion of confidential documents directly applies to R; no claim about destruction follows.");
        Add(db, "lsat", 4, 2, "Every funded project has an independent reviewer. No project with an independent reviewer is directed solely by its applicant. Project T is funded. Which inference is valid?", ["T is not directed solely by its applicant.", "T has no applicant.", "Every project not solely applicant-directed is funded."], 0, "Combine the two premises. Funding guarantees a reviewer, which rules out sole applicant direction.");
        Add(db, "lsat", 5, 2, "If a policy reduces congestion, the council retains it. The council did not retain policy P. What follows, assuming the rule holds?", ["P increased congestion.", "P did not reduce congestion.", "P was never tested."], 1, "Not retaining P rules out the sufficient condition of reducing congestion. Increasing congestion is stronger than the evidence permits.");
        Add(db, "lsat", 6, 2, "All valid appeals are filed within thirty days. No filing made within thirty days is automatically rejected as late. Appeal A is valid. What follows?", ["A will succeed.", "A has no defects.", "A is not automatically rejected as late."], 2, "Validity gives timely filing, which rules out this particular rejection. It does not establish success.");
        Add(db, "ucat", 1, 1, "All patients in group A received a reminder. Lee is in group A. Does it follow that Lee received a reminder?", ["Yes, it follows.", "No, it does not follow."], 0, "The universal statement includes Lee. No medical knowledge is needed.");
        Add(db, "ucat", 2, 1, "Every volunteer at a centre wears a badge. Sam wears a badge. Does it follow that Sam is a volunteer?", ["Yes, it follows.", "No, it does not follow."], 1, "Other people may wear badges. Reversing a one-way implication is not valid.");
        Add(db, "ucat", 3, 1, "No room on the ground floor has a balcony. Room J is on the ground floor. Does it follow that room J has no balcony?", ["Yes, it follows.", "No, it does not follow."], 0, "The exclusion applies to every room on that floor, including J.");
        Add(db, "ucat", 4, 2, "All morning appointments are confirmed. Every confirmed appointment has a reference number. Appointment L is in the morning. Does it follow that L has a reference number?", ["Yes, it follows.", "No, it does not follow."], 0, "The two rules form a chain from morning appointment to confirmation to reference number.");
        Add(db, "ucat", 5, 2, "All sealed packs are sterile. Some sterile packs are blue. Does it follow that some sealed packs are blue?", ["Yes, it follows.", "No, it does not follow."], 1, "The blue sterile packs need not be among the sealed packs. The overlap is not established.");
        Add(db, "ucat", 6, 2, "Every team leader completed training. No one who completed training is on the waiting list. Kim is on the waiting list. Does it follow that Kim is not a team leader?", ["Yes, it follows.", "No, it does not follow."], 0, "Being on the waiting list rules out completed training, which in turn rules out team leadership.");
        await db.SaveChangesAsync();
        await transaction.CommitAsync();
    }

    private static void Add(LabDbContext db, string pathway, int number, int difficulty, string prompt, string[] options, int answer, string explanation)
    {
        db.Questions.Add(new()
        {
            Key = $"{pathway}-{number}",
            BankId = pathway,
            PathwayId = pathway,
            Difficulty = difficulty,
            Prompt = prompt,
            Options = options,
            Answer = answer,
            Explanation = explanation,
            Misconception = "Check the direction of an implication; use only the stated premises.",
            Skills = [new() { SkillId = "deduction" }]
        });
    }
}
