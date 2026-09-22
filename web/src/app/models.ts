/** File purpose: Mirrors backend JSON contracts as TypeScript interfaces used throughout the Angular UI. */
export interface CssCategoryCoverage {
  category: string;
  count: number;
  percentage: number;
}

export interface PortfolioAnalysis {
  schemaVersion: string;
  evidenceBoundary: string;
  analyzedAt: string;
  tenantCount: number;
  modes: Array<{ mode: string; count: number }>;
  portfolioCoverage: CssCategoryCoverage[];
  commonPatterns: Array<{ target: string; category: string; tenantCount: number; tenantKeys: string[] }>;
  tokenPlan: DesignTokenPlan;
  tenants: Array<{
    tenantKey: string;
    displayName: string;
    sourceId: string;
    sourceVersion: string;
    migrationMode: string;
    cohortId: string;
    evidenceUnitCount: number;
    governedCoveragePercentage: number;
    reviewItemCount: number;
    unsafeItemCount: number;
    validationStatus: string;
  }>;
  exceptions: Array<{
    tenantKey: string;
    evidenceId: string;
    category: string;
    evidenceText: string;
    reason: string;
    sourceId: string;
    line: number;
    aiSuggestionAllowed: boolean;
  }>;
  failedChecks: Array<{ tenantKey: string; sourceId: string; code: string; message: string; line: number }>;
  modules: ModuleCoverage[];
}

export interface ModuleCoverage {
  module: string;
  tenantCount: number;
  totalDeclarationCount: number;
  candidateDeclarationCount: number;
  candidatePercentage: number;
  manualReviewCount: number;
}

export interface ZipImportResult {
  portfolio: unknown;
  analysis: PortfolioAnalysis;
  mappings: Array<{ tenantKey: string; displayName: string; files: string[]; cssCharacters: number }>;
}

export interface DesignTokenPlan {
  targetPercentage: number;
  totalDeclarationCount: number;
  existingCustomPropertyDeclarationCount: number;
  eligibleDeclarationCount: number;
  selectedDeclarationCount: number;
  eligibleCoveragePercentage: number;
  allDeclarationCoveragePercentage: number;
  targetReached: boolean;
  candidates: Array<{
    name: string;
    category: string;
    scope: string;
    cssProperty: string;
    declarationCount: number;
    tenantCount: number;
    portfolioSharePercentage: number;
    confidence: string;
    rationale: string;
    selectedForTarget: boolean;
    values: Array<{ tenantKey: string; value: string; declarationCount: number }>;
    exampleSelectors: string[];
  }>;
  tenantResiduals: Array<{
    tenantKey: string;
    totalDeclarationCount: number;
    eligibleDeclarationCount: number;
    coveredDeclarationCount: number;
    eligibleCoveragePercentage: number;
    items: Array<{
      selector: string;
      property: string;
      value: string;
      line: number;
      reason: string;
      suggestedStrategy: string;
      aiSuggestionAllowed: boolean;
    }>;
  }>;
}

export interface AiRecommendationResponse {
  provider: string;
  status: string;
  generatedAt: string;
  evidenceBoundary: string;
  message?: string;
  proposedContract?: {
    proposedTokenCount: number;
    aiConsolidatedTokenCount: number;
    deterministicRetainedTokenCount: number;
    coveredDeclarationCount: number;
    eligibleDeclarationCount: number;
    eligibleCoveragePercentage: number;
    allDeclarationCoveragePercentage: number;
    targetPercentage: number;
    targetReached: boolean;
    tokens: ProposedToken[];
    residualStrategies: Array<{
      pattern: string;
      recommendedTreatment: string;
      humanDecisionRequired: string;
      affectedTenants: string[];
    }>;
  };
  recommendations: Array<{
    priority: string;
    title: string;
    recommendation: string;
    evidence: string;
    affectedTokens: string[];
    affectedTenants: string[];
    humanDecisionRequired: string;
  }>;
}

export interface ProposedToken {
  name: string;
  category: string;
  purpose: string;
  rationale: string;
  suggestedDefault?: string;
  declarationCount: number;
  tenantCount: number;
  eligibleCoveragePercentage: number;
  origin: string;
  sourceCandidateNames: string[];
}

export interface TokenApprovalRow extends ProposedToken {
  approved: boolean;
  approvedName: string;
  approvedPurpose: string;
  approvedDefault: string;
  hasMeasuredValueConflict: boolean;
}

export interface ApprovedTokenSelection {
  name: string;
  purpose: string;
  approvedDefault?: string;
  sourceCandidateNames: string[];
  tenantValues?: Array<{ tenantKey: string; value: string }>;
}

export interface TenantConflictResolution {
  tenantKey: string;
  tokenName: string;
  strategy: 'merge' | 'split' | 'retain-raw';
  selectedValue?: string;
  splitValues?: Array<{ originalValue: string; tokenName: string }>;
}

export interface CssMigrationBundle {
  schemaVersion: string;
  contractVersion: string;
  generatedAt: string;
  evidenceBoundary: string;
  summary: {
    tenantCount: number;
    approvedTokenCount: number;
    totalDeclarationCount: number;
    tokenizedDeclarationCount: number;
    residualDeclarationCount: number;
    tokenizedPercentage: number;
    tenantValueConflictCount: number;
  };
  approvedTokens: ApprovedTokenSelection[];
  tenants: TenantMigrationArtifact[];
  modules: ModuleCoverage[];
  sharedAppCss: string;
}

export interface TenantMigrationArtifact {
  tenantKey: string;
  displayName: string;
  sourceId: string;
  sourceVersion: string;
  contractVersion: string;
  status: string;
  totalDeclarationCount: number;
  tokenizedDeclarationCount: number;
  residualDeclarationCount: number;
  unsafeDeclarationCount: number;
  tokenizedPercentage: number;
  originalCss: string;
  tokensCss: string;
  migratedCss: string;
  residualCss: string;
  tokenDefinitions: Array<{ name: string; value: string; source: string; sourceCandidateNames: string[] }>;
  valueConflicts: Array<{
    tokenName: string;
    values: string[];
    sourceCandidateNames: string[];
    resolution: string;
    draft: TenantConflictResolution;
  }>;
  appliedConflictResolutions: Array<{
    tokenName: string;
    strategy: string;
    outcome: string;
    selectedValue?: string;
    splitValues: Array<{ originalValue: string; tokenName: string }>;
    sourceCandidateNames: string[];
  }>;
  residuals: Array<{
    selector: string;
    property: string;
    value: string;
    line: number;
    reason: string;
    suggestedStrategy: string;
    unsafe: boolean;
  }>;
}

export interface SingleAnalysis {
  inventory: {
    summary: {
      styleRuleCount: number;
      declarationCount: number;
      diagnosticCount: number;
      errorCount: number;
      unsafeConstructCount: number;
    };
  };
  coverage: {
    policyVersion: string;
    summary: { evidenceUnitCount: number; parseErrorCount: number; categories: CssCategoryCoverage[] };
    assessments: Array<{
      evidenceId: string;
      category: string;
      evidenceText: string;
      reason: string;
      suggestedTargets: string[];
      requiresSemanticReview: boolean;
      aiSuggestionAllowed: boolean;
      location: { line: number };
    }>;
    unassessedDiagnostics: Array<{ code: string; message: string; location: { line: number } }>;
  };
}

export interface TenantThemeConfiguration {
  schemaVersion: string;
  tenantKey: string;
  displayName: string;
  source: 'new-client' | 'migrated-css' | string;
  basePreset: string;
  brand: {
    primaryColor: string;
    accentColor: string;
    backgroundColor: string;
    surfaceColor: string;
    textColor: string;
    fontFamily: string;
  };
  appearance: { cornerStyle: string; density: string };
  components: { cardVariant: string; navigationVariant: string; buttonVariant: string };
  legacyExceptions: Array<{ category: string; declarationCount: number; treatment: string; owner: string }>;
}

export interface ThemePreset {
  id: string;
  label: string;
  description: string;
  brand: TenantThemeConfiguration['brand'];
  appearance: TenantThemeConfiguration['appearance'];
  components: TenantThemeConfiguration['components'];
}

export interface ThemeCompilationResult {
  configuration: TenantThemeConfiguration;
  tokens: Array<{ name: string; value: string; sourceControl: string }>;
  tokensCss: string;
  diagnostics: Array<{ severity: string; code: string; message: string }>;
  publishable: boolean;
  legacyResidualDeclarationCount: number;
}

export interface MigratedThemeProjectionResult {
  configuration: TenantThemeConfiguration;
  projectionNotes: Array<{ severity: string; code: string; message: string }>;
  compilation: ThemeCompilationResult;
  projectionSummary: {
    resolvedTokenCount: number;
    mappedTokenCount: number;
    unmappedTokenCount: number;
    residualDeclarationCount: number;
    mappings: Array<{
      targetPath: string;
      label: string;
      status: 'mapped' | 'default-used' | 'unsupported-value' | string;
      appliedValue: string;
      sourceTokenName?: string;
      sourceValue?: string;
      explanation: string;
    }>;
    unmappedTokens: TenantMigrationArtifact['tokenDefinitions'];
  };
}
