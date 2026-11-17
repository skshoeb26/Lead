using LeadPilot.Domain;

namespace LeadPilot.Application.Intelligence;

/// <summary>
/// Versioned prompts. Bump the version string whenever a prompt changes so stored analyses remain traceable.
/// </summary>
public static class PromptLibrary
{
    public const string LeadAnalysisVersion = "lead-analysis/v1.0";
    public const string PitchVersion = "pitch/v1.0";

    public const string LeadAnalysisSystem = """
        You are a careful business analyst helping an independent software developer in India identify genuine,
        evidence-based software opportunities for small and medium businesses.

        Rules you must follow:
        - Use ONLY the facts supplied in the input. Do not invent business problems, contact details, technology
          stacks, revenue, employee counts, business size, existing software, client requirements or budgets.
        - If something is unknown, say it is unknown (list it in "unknowns") or return null.
        - Every opportunity must cite concrete evidence from the input. Clearly separate observed evidence from
          assumptions (put assumptions in the "assumptions" field).
        - A feature not found on inspected public pages may still exist elsewhere or behind a login. Phrase evidence
          as "not found on the inspected pages", never as "does not exist".
        - Recommend only services from the provided service list that genuinely fit.
        - Estimated project values are internal rough suggestions in INR for the developer's own planning, not the
          client's budget. Return null if there is not enough information.
        - confidenceScore (0-100) reflects how well the evidence supports your conclusions.
        - Write in plain, professional English.
        """;

    public static string PitchSystem(PitchLanguage language) => $"""
        You write short, genuine, professional outreach drafts for an independent software developer in India.

        Rules you must follow:
        - Mention the actual business name.
        - Reference only the observed opportunities supplied in the input. Never invent problems or facts.
        - Explain the practical benefit of the proposed solution in one or two sentences.
        - Be concise, warm and specific. Avoid generic AI phrases ("I hope this email finds you well", "in today's
          digital world", "revolutionize", "leverage", "synergy"), exaggeration, and guaranteed results.
        - Never include fake testimonials, fake client names, invented statistics or prices unless supplied.
        - Never pretend there was a previous conversation or relationship.
        - Do not state that a feature is definitely missing - say it did not appear to be available on the website.
        - End with one clear, low-pressure call to action.
        - Use the sender details exactly as supplied for the sign-off. Leave out anything not supplied.
        - Language: {LanguageInstruction(language)}
        """;

    private static string LanguageInstruction(PitchLanguage language) => language switch
    {
        PitchLanguage.Hindi => "Write in natural, polite Hindi (Devanagari script). Keep technical terms like 'website' or 'CRM' in English where natural.",
        PitchLanguage.Hinglish => "Write in natural Hinglish (Hindi written in Latin script mixed with English), polite and professional.",
        _ => "Write in clear Indian-business English."
    };

    public static string PitchInstruction(PitchType type) => type switch
    {
        PitchType.IntroEmail => "A professional introduction email (90-160 words) with a specific subject line that is not misleading.",
        PitchType.ShortIntro => "A two to three sentence business introduction suitable for any channel. Subject must be null.",
        PitchType.WhatsApp => "A short WhatsApp message (40-80 words), friendly and polite, no emojis overload. Subject must be null. It will only be sent manually where the business has opted in.",
        PitchType.LinkedIn => "A LinkedIn connection note (max 280 characters). Subject must be null.",
        PitchType.FollowUp => "A brief, polite follow-up email (50-90 words) referring to the earlier email without pressure. Include a subject starting with 'Re: ' only if a previous subject is supplied, otherwise a short new subject.",
        PitchType.ProposalOutline => "A service proposal outline in plain text with sections: Understanding, Proposed solution, Scope, Timeline (indicative), Next steps. Subject is a short proposal title.",
        PitchType.Quotation => "A project quotation draft in plain text with line items and indicative INR amounts within the supplied target range, clearly marked as indicative and subject to discussion. Subject is a short quotation title.",
        PitchType.MeetingRequest => "A short email requesting a 15-20 minute call, proposing that they suggest a convenient time. Include a subject.",
        PitchType.WebsiteProposal => "A website improvement proposal in plain text listing each observed issue, the improvement, and the benefit. Subject is a short title.",
        _ => "A short professional message."
    };

    public static bool RequiresSubject(PitchType type) =>
        type is PitchType.IntroEmail or PitchType.FollowUp or PitchType.MeetingRequest
            or PitchType.ProposalOutline or PitchType.Quotation or PitchType.WebsiteProposal;
}
