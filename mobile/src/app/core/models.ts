// API contracts (mirror the backend DTOs). Enums are serialised as strings.

export type PipelineStage =
  | 'New' | 'Analysed' | 'Qualified' | 'PitchReady' | 'Contacted' | 'Replied' | 'Interested' | 'MeetingScheduled'
  | 'ProposalSent' | 'Negotiation' | 'Won' | 'Lost' | 'NotInterested' | 'FollowUpRequired';
export const PIPELINE_STAGES: PipelineStage[] = [
  'New', 'Analysed', 'Qualified', 'PitchReady', 'Contacted', 'Replied', 'Interested', 'MeetingScheduled',
  'ProposalSent', 'Negotiation', 'Won', 'Lost', 'NotInterested', 'FollowUpRequired',
];
export type LeadPriority = 'Low' | 'NeedsReview' | 'Good' | 'High';
export type ProcessStatus = 'NotStarted' | 'Queued' | 'Running' | 'Completed' | 'Failed' | 'Skipped';
export type ContactType = 'Email' | 'Phone' | 'WhatsApp' | 'Website' | 'LinkedIn' | 'Other';
export type Confidence = 'Low' | 'Medium' | 'High';
export type EmailStatus = 'Draft' | 'PendingApproval' | 'Approved' | 'Queued' | 'Sending' | 'Sent' | 'Rejected' | 'Expired' | 'Failed' | 'OutcomeUnknown';
export type PitchType = 'IntroEmail' | 'ShortIntro' | 'WhatsApp' | 'LinkedIn' | 'FollowUp' | 'ProposalOutline' | 'Quotation' | 'MeetingRequest' | 'WebsiteProposal';
export type PitchLanguage = 'English' | 'Hindi' | 'Hinglish';
export type DiscoveryJobStatus = 'Pending' | 'Running' | 'Paused' | 'Completed' | 'Failed' | 'Cancelled';
export type FollowUpStatus = 'Pending' | 'Completed' | 'Snoozed' | 'Cancelled';
export type ActivityType = 'Note' | 'Call' | 'EmailSent' | 'EmailReceived' | 'StageChange' | 'Meeting' | 'Proposal' | 'FollowUp' | 'Analysis' | 'System' | 'WhatsApp' | 'LinkedIn';
export type Channel = 'Email' | 'WhatsApp' | 'Phone' | 'LinkedIn' | 'Sms';

export interface Paged<T> { items: T[]; page: number; pageSize: number; totalCount: number; totalPages: number; }

export interface User { id: string; email: string; displayName: string; role: string; }
export interface AuthResponse { accessToken: string; accessTokenExpiresAt: string; refreshToken: string; refreshTokenExpiresAt: string; user: User; }

export interface LeadListItem {
  id: string; businessName: string; categoryId?: string; categoryName?: string; city?: string; state?: string; pinCode?: string;
  websiteUrl?: string; phone?: string; email?: string; score?: number; priority?: LeadPriority; websiteQualityScore?: number;
  stage: PipelineStage; websiteAnalysisStatus: ProcessStatus; aiAnalysisStatus: ProcessStatus; rating?: number; reviewCount?: number;
  sourceProvider: string; discoveredAt: string; nextFollowUpAt?: string; updatedAt: string;
}
export interface Contact {
  id: string; type: ContactType; value: string; label?: string; isPrimary: boolean; sourceProvider: string; sourceUrl?: string; sourceRecordedAt: string;
  sourceVerified: boolean; syntaxVerified: boolean; deliveryVerified: boolean; consentVerified: boolean; suppressed: boolean;
}
export interface Opportunity {
  id: string; code: string; title: string; problem: string; evidence: string; assumptions?: string; recommendedService: string;
  confidence: Confidence; source: 'RuleEngine' | 'AI' | 'Manual'; isDismissed: boolean; createdAt: string;
}
export interface ScoreComponent { criterion: string; weight: number; points: number; fraction: number; reasons: string[]; }
export interface Score { total: number; priority: LeadPriority; calculatedAt: string; components: ScoreComponent[]; }
export interface Evidence { check: string; result: 'Pass' | 'Fail' | 'Warning' | 'Unknown' | 'NotApplicable'; observation: string; pageUrl?: string; }
export interface WebsiteAnalysis {
  id: string; status: ProcessStatus; requestedUrl: string; finalUrl?: string; httpStatusCode?: number; isReachable?: boolean; isHttps?: boolean;
  responseTimeMs?: number; hasViewportMeta?: boolean; title?: string; metaDescription?: string; hasContactPage?: boolean; hasEnquiryForm?: boolean;
  hasBookingFeature?: boolean; hasWhatsAppLink?: boolean; socialLinks: string[]; brokenLinksCount?: number; siteTypeGuess?: string;
  pageSpeedPerformance?: number; pageSpeedAccessibility?: number; pageSpeedSeo?: number; pagesInspected: string[]; error?: string; analysedAt?: string; evidence: Evidence[];
}
export interface AIAnalysis {
  id: string; status: ProcessStatus; provider: string; model: string; promptVersion: string; businessSummary?: string; suggestedApproach?: string;
  recommendedServices: string[]; estimatedValueMin?: number; estimatedValueMax?: number; confidenceScore?: number; nextAction?: string;
  unknowns: string[]; evidenceQuality?: string; inputTokens: number; outputTokens: number; costUsd: number; error?: string; createdAt: string; isMock: boolean;
}
export interface LeadDetail extends Omit<LeadListItem, 'categoryName'> {
  categoryName?: string; addressLine?: string; locality?: string; district?: string; latitude?: number; longitude?: number; providerPlaceId?: string;
  sourceUrl?: string; lastVerifiedAt?: string; businessStatus?: string; expectedDealValue?: number; actualDealValue?: number; lostReason?: string;
  nextAction?: string; lastContactedAt?: string; tags?: string; concurrencyStamp: string; createdAt: string;
  contacts: Contact[]; sources: { provider: string; externalId?: string; sourceUrl?: string; attribution?: string; retrievedAt: string }[];
  opportunities: Opportunity[]; latestScore?: Score; latestWebsiteAnalysis?: WebsiteAnalysis; latestAIAnalysis?: AIAnalysis;
}
export interface Category { id: string; name: string; slug: string; description?: string; searchKeywords?: string; relevantServices?: string; relevanceScore: number; isActive: boolean; isSystem: boolean; }

export interface DiscoveryLocation { state?: string; city?: string; district?: string; locality?: string; pinCode?: string; }
export interface DiscoveryQuery { categoryIds: string[]; keyword?: string; locations: DiscoveryLocation[]; websiteFilter: 'any' | 'with' | 'without'; requirePhone: boolean; minRating?: number; skipPermanentlyClosed: boolean; }
export interface CreateDiscoveryJob { name?: string; provider: string; query: DiscoveryQuery; maxResults: number; autoAnalyse: boolean; confirmCost: boolean; }
export interface DiscoveryEstimate { segments: number; estimatedRequests: number; estimatedCostUsd: number; requiresConfirmation: boolean; monthSpendUsd: number; monthlyBudgetUsd: number; provider: string; providerConfigured: boolean; providerIsMock: boolean; }
export interface DiscoveryJob {
  id: string; name: string; provider: string; status: DiscoveryJobStatus; maxResults: number; totalSegments: number; completedSegments: number;
  foundCount: number; createdCount: number; duplicateCount: number; skippedCount: number; errorCount: number; estimatedCostUsd: number; actualCostUsd: number;
  error?: string; createdAt: string; startedAt?: string; completedAt?: string; query: DiscoveryQuery; progress: number;
}
export interface DiscoveryResult { id: string; leadId?: string; businessName: string; outcome: 'Created' | 'Duplicate' | 'Skipped' | 'Error'; message?: string; createdAt: string; }
export interface ProviderStatus { name: string; displayName: string; configured: boolean; isMock: boolean; costPerRequestUsd: number; attribution: string; }

export interface Pitch {
  id: string; leadId: string; leadName: string; type: PitchType; language: PitchLanguage; subject?: string; body: string; provider?: string; model?: string;
  promptVersion?: string; isEdited: boolean; createdAt: string; updatedAt: string; opportunityIds: string[]; isMock: boolean;
}
export interface EmailDraft {
  id: string; leadId: string; leadName: string; toAddress: string; toName?: string; subject: string; body: string; contentHash: string; version: number;
  status: EmailStatus; approvedAt?: string; approvalExpiresAt?: string; sentAt?: string; senderAddress?: string; gmailMessageId?: string; gmailThreadId?: string;
  lastError?: string; rejectionReason?: string; concurrencyStamp: string; createdAt: string; updatedAt: string; recipientSourceRecorded: boolean; recipientSource?: string; testMode: boolean;
}
export interface EmailAudit { at: string; action: string; contentHash?: string; version?: number; details?: string; }
export interface SendCheck { canSend: boolean; problems: string[]; }

export interface Activity { id: string; leadId: string; leadName?: string; type: ActivityType; title: string; description?: string; occurredAt: string; }
export interface PipelineCard { id: string; businessName: string; city?: string; score?: number; priority?: LeadPriority; expectedDealValue?: number; nextFollowUpAt?: string; }
export interface PipelineColumn { stage: PipelineStage; count: number; expectedValue: number; leads: PipelineCard[]; }
export interface FollowUp { id: string; leadId: string; leadName: string; dueAt: string; status: FollowUpStatus; channel: Channel; note?: string; sequenceStep: number; emailDraftId?: string; completedAt?: string; isOverdue: boolean; }
export interface Meeting { id: string; leadId: string; leadName: string; scheduledAt: string; durationMinutes: number; location?: string; agenda?: string; outcome?: string; status: string; }
export interface Proposal { id: string; leadId: string; leadName: string; title: string; amount: number; currency: string; content?: string; status: string; sentAt?: string; validUntil?: string; }

export interface AppNotification { id: string; type: string; title: string; body: string; deepLink?: string; createdAt: string; readAt?: string; }

export interface DashboardSummary {
  totalLeads: number; newLeads: number; highPriorityLeads: number; analysedLeads: number; pendingApprovals: number; emailsSent: number; repliesRecorded: number;
  meetingsUpcoming: number; proposalsSent: number; projectsWon: number; revenueWon: number; followUpsDueToday: number; followUpsOverdue: number;
  outcomeUnknownEmails: number; sendingPaused: boolean; emailTestMode: boolean; unreadNotifications: number;
}
export interface RateMetric { value?: number; numerator: number; denominator: number; definition: string; }
export interface DashboardMetrics {
  from: string; to: string; leadsDiscoveredToday: number; leadsDiscoveredInRange: number; leadsDiscoveredThisMonth: number; leadsAnalysed: number; qualifiedLeads: number;
  highPriority: number; pitchDraftsGenerated: number; emailsApproved: number; emailsSent: number; repliesRecorded: number; meetingsScheduled: number; proposalsSent: number;
  projectsWon: number; totalDealValue: number; averageProjectValue?: number; replyRate: RateMetric; meetingRate: RateMetric; winRate: RateMetric;
  discoveryCostUsd: number; aiCostUsd: number; otherCostUsd: number; costPerQualifiedLeadUsd?: number;
  pipeline: { stage: PipelineStage; count: number }[]; leadsPerDay: { date: string; count: number }[];
}
export interface UsageSummary {
  aiMonthUsd: number; aiDayUsd: number; discoveryMonthUsd: number; otherMonthUsd: number; monthlyAIBudgetUsd: number; dailyAIBudgetUsd: number;
  monthlyDiscoveryBudgetUsd: number; hostingMonthUsd: number; aiInputTokensMonth: number; aiOutputTokensMonth: number; aiRequestsMonth: number;
  discoveryRequestsMonth: number; aiBudgetReached: boolean; discoveryBudgetReached: boolean; byProvider: { provider: string; requests: number; costUsd: number; failures: number }[];
}

export interface ScoringWeights { websiteGaps: number; serviceFit: number; contactAvailability: number; identifiedOpportunity: number; businessRelevance: number; budgetSuitability: number; highPriorityMin: number; goodPotentialMin: number; needsReviewMin: number; }
export interface OutreachSettings { pauseAllSending: boolean; dailySendLimit: number; hourlySendLimit: number; approvalValidityHours: number; requireSourceRecordedRecipient: boolean; requireEmailConsent: boolean; appendOptOutFooter: boolean; optOutFooter: string; followUpDays: number[]; autoCreateFollowUps: boolean; autoDraftFollowUps: boolean; }
export interface OwnerProfile { name: string; businessName: string; email?: string; phone?: string; portfolioUrl?: string; city?: string; signature: string; targetProjectMin: number; targetProjectMax: number; services: string[]; }
export interface BudgetSettings { monthlyAIBudgetUsd: number; dailyAIBudgetUsd: number; monthlyDiscoveryBudgetUsd: number; disableAIWhenBudgetReached: boolean; blockDiscoveryWhenBudgetReached: boolean; warnAtPercent: number; confirmDiscoveryAboveUsd: number; monthlyHostingCostUsd: number; }
export interface AutomationSettings { autoWebsiteAnalysisAfterDiscovery: boolean; autoAIAnalysisAfterWebsiteAnalysis: boolean; autoScoreAfterAnalysis: boolean; usePageSpeed: boolean; defaultPitchLanguage: string; }
export interface NotificationSettings { pushEnabled: boolean; includeDetailsInPush: boolean; discoveryCompleted: boolean; highPriorityLeads: boolean; analysisCompleted: boolean; approvalPending: boolean; emailFailed: boolean; followUpDue: boolean; jobErrors: boolean; }
export interface AllSettings { scoring: ScoringWeights; outreach: OutreachSettings; owner: OwnerProfile; budget: BudgetSettings; automation: AutomationSettings; notifications: NotificationSettings; }
export interface IntegrationStatus { key: string; name: string; configured: boolean; isMock: boolean; detail?: string; }
export interface SystemStatus { environment: string; emailTestMode: boolean; jobsEnabled: boolean; integrations: IntegrationStatus[]; version: string; }
export interface GoogleStatus { configured: boolean; connected: boolean; emailAddress?: string; scopes?: string; status?: string; connectedAt?: string; lastError?: string; testMode: boolean; }
export interface Suppression { id: string; channel: Channel; value: string; reason: string; source?: string; createdAt: string; }

export interface ProblemDetails { title?: string; status?: number; type?: string; errors?: Record<string, string[]>; }
