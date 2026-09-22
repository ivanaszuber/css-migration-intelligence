/** File purpose: Orchestrates navigation, Theme Studio, analysis, AI review, approvals, downloads, conflicts, and tenant drill-down. */
import { Component, ElementRef, ViewChild } from '@angular/core';
import { finalize } from 'rxjs';
import { CssIntelligenceApiService } from './css-intelligence-api.service';
import {
  AiRecommendationResponse,
  ApprovedTokenSelection,
  CssMigrationBundle,
  MigratedThemeProjectionResult,
  PortfolioAnalysis,
  SingleAnalysis,
  TenantConflictResolution,
  TenantMigrationArtifact,
  TenantThemeConfiguration,
  ThemeCompilationResult,
  ThemePreset,
  TokenApprovalRow,
  ZipImportResult
} from './models';

interface ConfigurableProperty {
  module: string;
  name: string;
  value: string;
  kind: 'color' | 'font' | 'image' | 'other';
  state: 'approved-token' | 'proposed-asset';
}

type ResidualItem = TenantMigrationArtifact['residuals'][number];
type ReviewAction = 'unreviewed' | 'align-to-kit' | 'retain-exception' | 'propose-kit-control';

interface ResidualDecision {
  action: ReviewAction;
  targetValue: string;
  note: string;
}

interface DemoModuleSettings {
  fontFamily: string;
  fontSize: string;
  fontStyle: string;
  imageName: string;
  imagePreviewUrl: string;
}

@Component({
  selector: 'cssi-root',
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent {
  @ViewChild('portfolioResults') private portfolioResults?: ElementRef<HTMLElement>;
  @ViewChild('reviewQueue') private reviewQueue?: ElementRef<HTMLElement>;
  @ViewChild('tenantDetail') private tenantDetail?: ElementRef<HTMLElement>;

  activeWorkspace: 'studio' | 'migration' | 'inventory' = 'migration';
  migrationStep: 1 | 2 | 3 | 4 | 5 = 1;
  inputMode: 'demo' | 'single' | 'json' = 'demo';
  themeSection: 'brand' | 'appearance' | 'components' = 'brand';

  portfolio?: PortfolioAnalysis;
  aiRecommendations?: AiRecommendationResponse;
  approvalRows: TokenApprovalRow[] = [];
  migrationBundle?: CssMigrationBundle;
  selectedTenant?: TenantMigrationArtifact;
  contractVersion = '1.0';
  generatingBundle = false;
  conflictResolutionDrafts: TenantConflictResolution[] = [];
  themePresets: ThemePreset[] = [];
  themeConfiguration?: TenantThemeConfiguration;
  themeCompilation?: ThemeCompilationResult;
  themeProjectionNotes: MigratedThemeProjectionResult['projectionNotes'] = [];
  themeProjectionSummary?: MigratedThemeProjectionResult['projectionSummary'];
  themeBusy = false;
  themeStatus = '';
  single?: SingleAnalysis;
  loadingPortfolio = false;
  analysingCss = false;
  requestingAi = false;
  error = '';
  portfolioStatus = '';
  zipMappings: ZipImportResult['mappings'] = [];
  uploadApproved = false;
  selectedModule = 'Home & communications';
  reviewTenantKey = 'all';
  reviewLimit = 50;
  reviewDecisions: Record<string, ResidualDecision> = {};
  private reviewItemsCacheKey = '';
  private reviewItemsCache: Array<{ tenant: string; tenantKey: string; item: ResidualItem }> = [];
  private readonly demoSettings: Record<string, DemoModuleSettings> = {};
  candidateFilter = 'all';
  candidateLimit = 30;
  configurableProperties: ConfigurableProperty[] = [];
  private activePortfolioInput?: unknown;
  sourceId = 'operator-synthetic.css';
  css = `.app-header { color: #ffffff; background: #18231f; padding: 16px 20px; }
.primary-button { color: #ffffff; background: #e24a32; border-radius: 9px; padding: 12px 18px; }
.campaign-card { position: fixed; right: 3px; bottom: 12px; width: calc(100% - 24px); }`;
  portfolioJson = `{
  "schemaVersion": "1.0",
  "exportedAt": "2026-08-01T12:00:00Z",
  "sources": [
    {
      "tenantKey": "synthetic-demo",
      "displayName": "Synthetic Demo",
      "sourceId": "theme.css",
      "version": "1",
      "updatedAt": "2026-08-01T11:00:00Z",
      "css": ".app-header { color: white; background: #7257ff; padding: 16px; } .mobile-nav { display: grid; grid-template-columns: repeat(5, 1fr); } .primary-button { color: white; background: #7257ff; border-radius: 8px; padding: 12px 18px; }"
    }
  ]
}`;

  constructor(private readonly api: CssIntelligenceApiService) {}

  startNewTheme(preset: ThemePreset): void {
    this.themeConfiguration = {
      schemaVersion: '1.0',
      tenantKey: 'new-tenant',
      displayName: 'New tenant',
      source: 'new-client',
      basePreset: preset.id,
      brand: { ...preset.brand },
      appearance: { ...preset.appearance },
      components: { ...preset.components },
      legacyExceptions: []
    };
    this.themeProjectionNotes = [];
    this.themeProjectionSummary = undefined;
    this.themeStatus = `${preset.label} loaded as a new-client starting point.`;
    this.compileTheme();
  }

  setWorkspace(workspace: 'studio' | 'migration' | 'inventory'): void {
    this.activeWorkspace = workspace;
    if (workspace === 'studio' && this.selectedModule === 'Shared app')
      this.selectedModule = 'Home & communications';
    window.scrollTo({ top: 0, behavior: 'smooth' });
  }

  setMigrationStep(step: 1 | 2 | 3 | 4 | 5): void {
    this.migrationStep = step;
    requestAnimationFrame(() => window.scrollTo({ top: 0, behavior: 'smooth' }));
  }

  importSelectedTenantTheme(): void {
    const tenant = this.selectedTenant;
    if (!tenant) {
      this.error = 'Generate a migration bundle and select a tenant before importing its theme evidence.';
      return;
    }
    this.themeBusy = true;
    this.error = '';
    this.api.projectThemeFromMigration({
      tenantKey: tenant.tenantKey,
      displayName: tenant.displayName,
      tokenDefinitions: tenant.tokenDefinitions,
      residuals: tenant.residuals
    }).pipe(finalize(() => this.themeBusy = false)).subscribe({
      next: result => {
        this.themeConfiguration = result.configuration;
        this.themeCompilation = result.compilation;
        this.themeProjectionNotes = result.projectionNotes;
        this.themeProjectionSummary = result.projectionSummary;
        this.themeStatus = `${tenant.displayName} loaded: ${result.projectionSummary.mappedTokenCount} resolved tokens mapped into ThemeConfiguration v1, ${result.projectionSummary.unmappedTokenCount} resolved tokens remain outside the schema, and ${result.projectionSummary.residualDeclarationCount} raw declarations remain quarantined.`;
      },
      error: error => this.error = this.errorMessage(error, 'The migrated tenant could not be projected into Theme Studio.')
    });
  }

  openSelectedTenantInThemeStudio(): void {
    if (!this.selectedTenant) return;
    this.activeWorkspace = 'studio';
    this.themeSection = 'brand';
    window.scrollTo({ top: 0, behavior: 'smooth' });
    this.importSelectedTenantTheme();
  }

  compileTheme(): void {
    if (!this.themeConfiguration) return;
    this.themeBusy = true;
    this.error = '';
    this.api.compileTheme(this.themeConfiguration).pipe(finalize(() => this.themeBusy = false)).subscribe({
      next: result => {
        this.themeCompilation = result;
        this.themeStatus = result.publishable
          ? 'Theme validated and compiled into the runtime token contract.'
          : 'Theme compiled, but validation errors must be resolved before publishing.';
      },
      error: error => this.error = this.errorMessage(error, 'The theme configuration could not be compiled.')
    });
  }

  previewStyles(): Record<string, string> {
    const theme = this.themeConfiguration;
    if (!theme) return {};
    const radius = theme.appearance.cornerStyle === 'square' ? '0px' : theme.appearance.cornerStyle === 'rounded' ? '18px' : '10px';
    const gap = theme.appearance.density === 'compact' ? '10px' : theme.appearance.density === 'spacious' ? '22px' : '16px';
    return {
      '--preview-primary': theme.brand.primaryColor,
      '--preview-accent': theme.brand.accentColor,
      '--preview-background': theme.brand.backgroundColor,
      '--preview-surface': theme.brand.surfaceColor,
      '--preview-text': theme.brand.textColor,
      '--preview-font': theme.brand.fontFamily,
      '--preview-radius': radius,
      '--preview-gap': gap
    };
  }

  downloadThemeConfiguration(): void {
    if (this.themeConfiguration)
      this.downloadJson(`${this.themeConfiguration.tenantKey}.theme-configuration.json`, this.themeConfiguration);
  }

  downloadRuntimeTheme(): void {
    if (this.themeCompilation)
      this.downloadText(`${this.themeCompilation.configuration.tenantKey}.runtime-theme.css`, this.themeCompilation.tokensCss, 'text/css');
  }

  loadBundledPortfolio(): void {
    this.loadingPortfolio = true;
    this.error = '';
    this.portfolioStatus = '';
    this.api.loadBundledPortfolio().pipe(finalize(() => this.loadingPortfolio = false)).subscribe({
      next: result => {
        this.activePortfolioInput = undefined;
        this.showPortfolio(result, 'Built-in demo');
      },
      error: error => this.error = this.errorMessage(error, 'The bundled portfolio could not be loaded.')
    });
  }

  runDemoZip(): void {
    this.loadingPortfolio = true;
    this.error = '';
    this.api.loadDemoZip().pipe(finalize(() => this.loadingPortfolio = false)).subscribe({
      next: result => this.acceptZip(result, 'Synthetic demo ZIP'),
      error: error => this.error = this.errorMessage(error, 'The synthetic demo ZIP could not be loaded.')
    });
  }

  uploadZip(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    if (!this.uploadApproved) {
      this.error = 'Confirm that this is synthetic or explicitly approved CSS before uploading.';
      input.value = '';
      return;
    }
    this.loadingPortfolio = true;
    this.error = '';
    this.api.uploadZip(file).pipe(finalize(() => {
      this.loadingPortfolio = false;
      input.value = '';
    })).subscribe({
      next: result => this.acceptZip(result, file.name),
      error: error => this.error = this.errorMessage(error, 'The ZIP could not be analysed.')
    });
  }

  private acceptZip(result: ZipImportResult, source: string): void {
    this.activePortfolioInput = result.portfolio;
    this.zipMappings = result.mappings;
    this.showPortfolio(result.analysis, source);
    this.prepareDeterministicContract();
  }

  prepareDeterministicContract(): void {
    if (!this.portfolio) return;
    this.approvalRows = this.portfolio.tokenPlan.candidates.map(candidate => {
      const hasMeasuredValueConflict = this.hasMeasuredValueConflict([candidate.name]);
      return {
        name: candidate.name,
        category: candidate.category,
        purpose: `${candidate.scope} ${candidate.cssProperty} configurable property`,
        rationale: candidate.rationale,
        declarationCount: candidate.declarationCount,
        tenantCount: candidate.tenantCount,
        eligibleCoveragePercentage: candidate.portfolioSharePercentage,
        origin: 'deterministic',
        sourceCandidateNames: [candidate.name],
        approved: candidate.selectedForTarget && !hasMeasuredValueConflict,
        approvedName: candidate.name,
        approvedPurpose: `${candidate.scope} ${candidate.cssProperty} configurable property`,
        approvedDefault: '',
        hasMeasuredValueConflict
      };
    });
  }

  candidateEvidence(name: string): string {
    const candidate = this.portfolio?.tokenPlan.candidates.find(item => item.name === name);
    return candidate?.exampleSelectors.join(', ') || 'No example selector';
  }

  candidateModule(name: string): string {
    if (name.includes('module-home')) return 'Home & communications';
    if (name.includes('module-learning')) return 'Learning';
    if (name.includes('module-operations')) return 'Operations';
    return 'Shared app';
  }

  candidateTenantBreakdown(sourceCandidateNames: string[]): Array<{
    tenantKey: string; displayName: string; values: string[]; declarationCount: number
  }> {
    if (!this.portfolio) return [];
    const candidates = this.portfolio.tokenPlan.candidates
      .filter(candidate => sourceCandidateNames.includes(candidate.name));
    return this.portfolio.tenants.map(tenant => {
      const entries = candidates.flatMap(candidate => candidate.values
        .filter(value => value.tenantKey === tenant.tenantKey));
      return {
        tenantKey: tenant.tenantKey,
        displayName: tenant.displayName,
        values: [...new Set(entries.map(entry => entry.value))],
        declarationCount: entries.reduce((sum, entry) => sum + entry.declarationCount, 0)
      };
    }).filter(item => item.declarationCount > 0);
  }

  conflictingTenants(sourceCandidateNames: string[]): string {
    return this.candidateTenantBreakdown(sourceCandidateNames)
      .filter(item => new Set(item.values.map(value => value.toLowerCase())).size > 1)
      .map(item => item.displayName).join(', ');
  }

  filteredCandidates(): TokenApprovalRow[] {
    if (this.candidateFilter === 'all') return this.approvalRows;
    if (this.candidateFilter === 'shared')
      return this.approvalRows.filter(item => !item.name.includes('module-home')
        && !item.name.includes('module-learning') && !item.name.includes('module-operations'));
    return this.approvalRows.filter(item => item.name.includes(`module-${this.candidateFilter}`));
  }

  visibleCandidates(): TokenApprovalRow[] {
    return this.filteredCandidates().slice(0, this.candidateLimit);
  }

  setCandidateFilter(filter: string): void {
    this.candidateFilter = filter;
    this.candidateLimit = 30;
  }

  moduleReviewItems(): Array<{ tenant: string; tenantKey: string; item: ResidualItem }> {
    if (!this.migrationBundle) return [];
    const cacheKey = `${this.migrationBundle.generatedAt}\n${this.reviewTenantKey}\n${this.selectedModule}`;
    if (this.reviewItemsCacheKey === cacheKey) return this.reviewItemsCache;
    const prefix = this.selectedModule === 'Home & communications' ? '.module-home'
      : this.selectedModule === 'Learning' ? '.module-learning'
      : this.selectedModule === 'Operations' ? '.module-operations' : '';
    this.reviewItemsCache = this.migrationBundle.tenants.filter(tenant => this.reviewTenantKey === 'all' || tenant.tenantKey === this.reviewTenantKey)
      .flatMap(tenant => tenant.residuals
      .filter(item => prefix ? item.selector.includes(prefix)
        : !['.module-home', '.module-learning', '.module-operations'].some(name => item.selector.includes(name)))
      .map(item => ({ tenant: tenant.displayName, tenantKey: tenant.tenantKey, item })));
    this.reviewItemsCacheKey = cacheKey;
    return this.reviewItemsCache;
  }

  setReviewTenant(tenantKey: string): void {
    this.reviewTenantKey = tenantKey;
    this.reviewLimit = 50;
  }

  reviewTenantName(): string {
    if (this.reviewTenantKey === 'all') return 'all clients';
    return this.migrationBundle?.tenants.find(tenant => tenant.tenantKey === this.reviewTenantKey)?.displayName ?? 'selected client';
  }

  setReviewModule(module: string): void {
    this.selectedModule = module;
    this.reviewLimit = 50;
  }

  residualKey(tenantKey: string, item: ResidualItem): string {
    return `${tenantKey}\n${item.selector}\n${item.property}\n${item.line}\n${item.value}`;
  }

  decisionFor(tenantKey: string, item: ResidualItem): ResidualDecision {
    const key = this.residualKey(tenantKey, item);
    return this.reviewDecisions[key] ??= { action: 'unreviewed', targetValue: '', note: '' };
  }

  reviewedCount(): number {
    return Object.values(this.reviewDecisions).filter(item => item.action !== 'unreviewed').length;
  }

  plainResidualReason(item: ResidualItem): string {
    if (item.unsafe || item.reason.includes('unsafe')) return 'Needs engineering review before any automated change.';
    if (item.reason.includes('not repeated enough')) return 'This value did not enter the approved common-property set. It may still be valid.';
    if (item.reason.includes('structure or behaviour')) return 'This affects layout or behaviour, not just a configurable visual value.';
    if (item.reason.includes('dynamic or references')) return 'This depends on another value, so the importer cannot safely choose a replacement.';
    if (item.reason.includes('conflicting')) return 'Multiple values need an explicit human choice.';
    return 'No approved configuration property replaced this declaration yet.';
  }

  downloadReviewPlan(): void {
    if (!this.migrationBundle) return;
    const decisions = this.migrationBundle.tenants.flatMap(tenant => tenant.residuals
      .map(item => ({ tenantKey: tenant.tenantKey, selector: item.selector, property: item.property,
        currentValue: item.value, line: item.line, ...this.decisionFor(tenant.tenantKey, item) }))
      .filter(item => item.action !== 'unreviewed'));
    this.downloadJson('residual-review-draft.json', {
      status: 'local-draft-not-approved-or-published',
      contractVersion: this.migrationBundle.contractVersion,
      decisions
    });
  }

  scrollTenantDetail(): void {
    this.tenantDetail?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' });
  }

  moduleRows() {
    return this.migrationBundle?.modules ?? this.portfolio?.modules ?? [];
  }

  propertiesForSelectedModule(): ConfigurableProperty[] {
    return this.configurableProperties.filter(item => item.module === this.selectedModule);
  }

  saveConfigurationDraft(): void {
    if (!this.selectedTenant) return;
    this.downloadJson(`${this.selectedTenant.tenantKey}.module-configuration-draft.json`, {
      status: 'local-demo-draft-not-published',
      tenantKey: this.selectedTenant.tenantKey,
      module: this.selectedModule,
      properties: this.propertiesForSelectedModule(),
      demonstrationControls: {
        fontFamily: this.currentDemoSettings().fontFamily,
        fontSize: this.currentDemoSettings().fontSize,
        fontStyle: this.currentDemoSettings().fontStyle,
        imageName: this.currentDemoSettings().imageName,
        imageStatus: this.currentDemoSettings().imageName ? 'local-preview-only-asset-not-uploaded' : 'not-selected'
      }
    });
  }

  currentDemoSettings(): DemoModuleSettings {
    const key = `${this.selectedTenant?.tenantKey ?? 'none'}\n${this.selectedModule}`;
    if (!this.demoSettings[key]) {
      this.demoSettings[key] = {
        fontFamily: this.previewValue('font-family', 'Arial, sans-serif'),
        fontSize: this.previewValue('title-font-size', '28px'),
        fontStyle: 'normal',
        imageName: '',
        imagePreviewUrl: ''
      };
    }
    return this.demoSettings[key];
  }

  syncDemoTypography(): void {
    const settings = this.currentDemoSettings();
    const font = this.propertiesForSelectedModule().find(item => item.name.endsWith('-font-family'));
    const size = this.propertiesForSelectedModule().find(item => item.name.endsWith('-title-font-size'));
    if (font) font.value = settings.fontFamily;
    if (size) size.value = settings.fontSize;
  }

  chooseDemoImage(event: Event): void {
    const input = event.target as HTMLInputElement;
    const file = input.files?.[0];
    if (!file) return;
    if (!['image/png', 'image/jpeg', 'image/webp'].includes(file.type) || file.size > 5_000_000) {
      this.error = 'Choose a PNG, JPEG, or WebP image smaller than 5 MB for the local preview.';
      input.value = '';
      return;
    }
    const settings = this.currentDemoSettings();
    if (settings.imagePreviewUrl) URL.revokeObjectURL(settings.imagePreviewUrl);
    settings.imagePreviewUrl = URL.createObjectURL(file);
    settings.imageName = file.name;
  }

  previewValue(fragment: string, fallback: string): string {
    return this.propertiesForSelectedModule().find(item => item.name.includes(fragment))?.value || fallback;
  }

  analyzePortfolioJson(): void {
    this.loadingPortfolio = true;
    this.error = '';
    this.portfolioStatus = '';
    try {
      const parsed: unknown = JSON.parse(this.portfolioJson);
      this.api.analyzePortfolio(parsed).pipe(finalize(() => this.loadingPortfolio = false)).subscribe({
        next: result => {
          this.activePortfolioInput = parsed;
          this.showPortfolio(result, 'Pasted JSON');
        },
        error: error => this.error = this.errorMessage(error, 'The JSON portfolio could not be analysed.')
      });
    } catch {
      this.loadingPortfolio = false;
      this.error = 'The portfolio JSON is not valid JSON.';
    }
  }

  requestAiRecommendations(): void {
    this.requestingAi = true;
    this.error = '';
    const request = this.activePortfolioInput === undefined
      ? this.api.recommendForBundledPortfolio()
      : this.api.recommendForPortfolio(this.activePortfolioInput);
    request.pipe(finalize(() => this.requestingAi = false)).subscribe({
      next: result => {
        this.aiRecommendations = result;
        this.migrationBundle = undefined;
        this.selectedTenant = undefined;
        this.conflictResolutionDrafts = [];
        this.initializeApprovals(result);
        if (this.approvalRows.length === 0) {
          this.migrationStep = 3;
          this.error = result.message || 'AI returned no proposed token contract. Review the AI configuration and try again.';
          return;
        }
        this.migrationStep = 4;
      },
      error: error => this.error = this.errorMessage(error, 'Azure AI recommendations could not be generated.')
    });
  }

  generateMigrationBundle(): void {
    const tokens = this.approvedTokens();
    if (tokens.length === 0) {
      this.error = 'Approve at least one token before generating migration artifacts.';
      return;
    }

    this.generatingBundle = true;
    this.error = '';
    this.api.generateMigrationBundle({
      portfolio: this.activePortfolioInput ?? null,
      contractVersion: this.contractVersion,
      tokens,
      conflictResolutions: this.conflictResolutionDrafts
    }).pipe(finalize(() => this.generatingBundle = false)).subscribe({
      next: bundle => {
        const selectedTenantKey = this.selectedTenant?.tenantKey;
        this.initializeConflictResolutions(bundle);
        this.migrationBundle = bundle;
        this.selectedTenant = bundle.tenants.find(tenant => tenant.tenantKey === selectedTenantKey) ?? bundle.tenants[0];
        this.reviewTenantKey = 'all';
        this.reviewLimit = 50;
        this.buildConfigurableProperties();
        this.migrationStep = 5;
        this.activeWorkspace = 'inventory';
      },
      error: error => this.error = this.errorMessage(error, 'The migration bundle could not be generated.')
    });
  }

  approveAllSafe(): void {
    this.approvalRows.forEach(row => row.approved = !row.hasMeasuredValueConflict);
  }

  approveAllForGuidedResolution(): void {
    this.approvalRows.forEach(row => row.approved = true);
    this.migrationBundle = undefined;
    this.selectedTenant = undefined;
    this.conflictResolutionDrafts = [];
  }

  clearApprovals(): void {
    this.approvalRows.forEach(row => row.approved = false);
    this.migrationBundle = undefined;
    this.selectedTenant = undefined;
    this.conflictResolutionDrafts = [];
  }

  approvedCount(): number {
    return this.approvalRows.filter(row => row.approved).length;
  }

  selectTenant(tenant: TenantMigrationArtifact): void {
    this.selectedTenant = tenant;
    this.setReviewTenant(tenant.tenantKey);
    this.buildConfigurableProperties();
    this.activeWorkspace = 'inventory';
    requestAnimationFrame(() => this.reviewQueue?.nativeElement.scrollIntoView({ behavior: 'smooth', block: 'start' }));
  }

  private buildConfigurableProperties(): void {
    const tenant = this.selectedTenant;
    if (!tenant) return;
    const modules: Array<[string, string]> = [
      ['Home & communications', 'module-home'],
      ['Learning', 'module-learning'],
      ['Operations', 'module-operations']
    ];
    this.configurableProperties = modules.flatMap(([module, prefix]) => {
      const tokenRows: ConfigurableProperty[] = tenant.tokenDefinitions
        .filter(item => item.name.includes(prefix))
        .map(item => ({
          module,
          name: item.name,
          value: item.value,
          kind: item.name.startsWith('--color-') && /^#[0-9a-f]{6}$/i.test(item.value) ? 'color' as const
            : item.name.startsWith('--font-') ? 'font' as const : 'other' as const,
          state: 'approved-token' as const
        }));
      const assets: ConfigurableProperty[] = tenant.residuals
        .filter(item => item.selector.includes(`.${prefix}`) && item.property === 'background-image')
        .map(item => ({ module, name: `${item.selector} image`, value: item.value,
          kind: 'image' as const, state: 'proposed-asset' as const }));
      return [...tokenRows, ...assets];
    });
  }

  applyConflictResolutions(): void {
    const invalid = this.conflictResolutionDrafts.find(draft =>
      (draft.strategy === 'merge' && !draft.selectedValue)
      || (draft.strategy === 'split' && (
        !draft.splitValues?.length
        || draft.splitValues.some(item => !item.tokenName.trim().startsWith('--'))
        || new Set(draft.splitValues.map(item => item.tokenName.trim())).size !== draft.splitValues.length
      )));
    if (invalid) {
      this.error = `Complete the ${invalid.strategy} decision for ${invalid.tokenName} before regenerating.`;
      return;
    }
    this.generateMigrationBundle();
  }

  downloadApprovedContract(): void {
    this.downloadJson(`approved-token-contract-v${this.contractVersion}.json`, {
      schemaVersion: '1.0',
      contractVersion: this.contractVersion,
      status: 'approved-local-artifact',
      approvedAt: new Date().toISOString(),
      tokens: this.approvedTokens(),
      conflictResolutions: this.conflictResolutionDrafts
    });
  }

  downloadBundle(): void {
    if (this.migrationBundle)
      this.downloadJson(`css-migration-bundle-v${this.migrationBundle.contractVersion}.json`, this.migrationBundle);
  }

  downloadSharedAppCss(): void {
    if (this.migrationBundle)
      this.downloadText('shared-ui-kit-prototype.css', this.migrationBundle.sharedAppCss, 'text/css');
  }

  downloadTenantFile(kind: 'tokens' | 'migrated' | 'residual' | 'manifest'): void {
    const tenant = this.selectedTenant;
    if (!tenant) return;
    if (kind === 'tokens') this.downloadText(`${tenant.tenantKey}.tokens.css`, tenant.tokensCss, 'text/css');
    if (kind === 'migrated') this.downloadText(`${tenant.tenantKey}.migrated.css`, tenant.migratedCss, 'text/css');
    if (kind === 'residual') this.downloadText(`${tenant.tenantKey}.residual.css`, tenant.residualCss, 'text/css');
    if (kind === 'manifest') this.downloadJson(`${tenant.tenantKey}.migration-manifest.json`, {
      tenantKey: tenant.tenantKey,
      sourceId: tenant.sourceId,
      sourceVersion: tenant.sourceVersion,
      contractVersion: tenant.contractVersion,
      status: tenant.status,
      totalDeclarationCount: tenant.totalDeclarationCount,
      tokenizedDeclarationCount: tenant.tokenizedDeclarationCount,
      residualDeclarationCount: tenant.residualDeclarationCount,
      unsafeDeclarationCount: tenant.unsafeDeclarationCount,
      tokenizedPercentage: tenant.tokenizedPercentage,
      tokenDefinitions: tenant.tokenDefinitions,
      valueConflicts: tenant.valueConflicts,
      appliedConflictResolutions: tenant.appliedConflictResolutions,
      residuals: tenant.residuals
    });
  }

  analyzeCss(): void {
    this.analysingCss = true;
    this.error = '';
    this.api.analyzeCss(this.sourceId, this.css).pipe(finalize(() => this.analysingCss = false)).subscribe({
      next: result => this.single = result,
      error: error => this.error = this.errorMessage(error, 'The CSS could not be analysed.')
    });
  }

  clear(): void {
    this.css = '';
    this.portfolioJson = '';
    this.portfolio = undefined;
    this.single = undefined;
    this.aiRecommendations = undefined;
    this.approvalRows = [];
    this.migrationBundle = undefined;
    this.selectedTenant = undefined;
    this.conflictResolutionDrafts = [];
    this.activePortfolioInput = undefined;
    this.error = '';
    this.portfolioStatus = '';
  }

  aiChangedTokens(contract: NonNullable<AiRecommendationResponse['proposedContract']>) {
    return contract.tokens.filter(token => token.origin === 'ai-consolidated-or-renamed');
  }

  private initializeApprovals(result: AiRecommendationResponse): void {
    const tokens = result.proposedContract?.tokens ?? [];
    this.approvalRows = tokens.map(token => {
      const hasMeasuredValueConflict = this.hasMeasuredValueConflict(token.sourceCandidateNames);
      return {
        ...token,
        approved: !hasMeasuredValueConflict,
        approvedName: token.name,
        approvedPurpose: token.purpose,
        approvedDefault: token.suggestedDefault ?? '',
        hasMeasuredValueConflict
      };
    });
  }

  private hasMeasuredValueConflict(sourceCandidateNames: string[]): boolean {
    return this.conflictingTenants(sourceCandidateNames).length > 0;
  }

  private approvedTokens(): ApprovedTokenSelection[] {
    return this.approvalRows.filter(row => row.approved).map(row => ({
      name: row.approvedName.trim(),
      purpose: row.approvedPurpose.trim(),
      approvedDefault: row.approvedDefault.trim() || undefined,
      sourceCandidateNames: row.sourceCandidateNames
    }));
  }

  private initializeConflictResolutions(bundle: CssMigrationBundle): void {
    const existing = new Map(this.conflictResolutionDrafts.map(draft => [this.conflictKey(draft.tenantKey, draft.tokenName), draft]));
    for (const tenant of bundle.tenants) {
      for (const conflict of tenant.valueConflicts) {
        const key = this.conflictKey(tenant.tenantKey, conflict.tokenName);
        const draft = existing.get(key) ?? {
          tenantKey: tenant.tenantKey,
          tokenName: conflict.tokenName,
          strategy: 'merge' as const,
          selectedValue: conflict.values[0],
          splitValues: conflict.values.map((value, index) => ({
            originalValue: value,
            tokenName: `${conflict.tokenName}-option-${index + 1}`
          }))
        };
        conflict.draft = draft;
        existing.set(key, draft);
      }
    }
    this.conflictResolutionDrafts = [...existing.values()];
  }

  private conflictKey(tenantKey: string, tokenName: string): string {
    return `${tenantKey}\n${tokenName}`;
  }

  private downloadJson(fileName: string, value: unknown): void {
    this.downloadText(fileName, JSON.stringify(value, null, 2), 'application/json');
  }

  private downloadText(fileName: string, value: string, type: string): void {
    const url = URL.createObjectURL(new Blob([value], { type }));
    const anchor = document.createElement('a');
    anchor.href = url;
    anchor.download = fileName;
    anchor.click();
    URL.revokeObjectURL(url);
  }

  private showPortfolio(result: PortfolioAnalysis, source: string): void {
    this.portfolio = result;
    this.aiRecommendations = undefined;
    this.approvalRows = [];
    this.migrationBundle = undefined;
    this.selectedTenant = undefined;
    this.conflictResolutionDrafts = [];
    this.portfolioStatus = `${source} analysed: ${result.tenantCount} clients. Mapping and coverage are ready for review.`;
    this.activeWorkspace = 'migration';
    this.migrationStep = 1;
    requestAnimationFrame(() => {
      const results = this.portfolioResults?.nativeElement;
      results?.scrollIntoView({ behavior: 'smooth', block: 'start' });
      results?.focus({ preventScroll: true });
    });
  }

  private errorMessage(error: { error?: { errors?: Array<{ message?: string }>; detail?: string; title?: string } }, fallback: string): string {
    return error.error?.errors?.map(item => item.message).filter(Boolean).join(' ')
      || error.error?.detail
      || error.error?.title
      || fallback;
  }
}
