/** File purpose: Wraps every backend HTTP call and selects the local or hosted same-origin API base URL. */
import { Injectable } from '@angular/core';
import { HttpClient } from '@angular/common/http';
import { Observable } from 'rxjs';
import {
  AiRecommendationResponse,
  CssMigrationBundle,
  MigratedThemeProjectionResult,
  PortfolioAnalysis,
  SingleAnalysis,
  TenantThemeConfiguration,
  ThemeCompilationResult,
  ThemePreset
} from './models';
import { ZipImportResult } from './models';

@Injectable({ providedIn: 'root' })
export class CssIntelligenceApiService {
  private readonly apiBase = window.location.hostname === 'localhost'
    ? 'http://localhost:5250'
    : window.location.origin;

  constructor(private readonly http: HttpClient) {}

  loadBundledPortfolio(): Observable<PortfolioAnalysis> {
    return this.http.get<PortfolioAnalysis>(`${this.apiBase}/api/portfolio`);
  }

  analyzePortfolio(input: unknown): Observable<PortfolioAnalysis> {
    return this.http.post<PortfolioAnalysis>(`${this.apiBase}/api/portfolio/analyze`, input);
  }

  loadDemoZip(): Observable<ZipImportResult> {
    return this.http.get<ZipImportResult>(`${this.apiBase}/api/portfolio/demo-import`);
  }

  uploadZip(file: File): Observable<ZipImportResult> {
    const body = new FormData();
    body.append('file', file);
    return this.http.post<ZipImportResult>(`${this.apiBase}/api/portfolio/import-zip`, body);
  }

  analyzeCss(sourceId: string, css: string): Observable<SingleAnalysis> {
    return this.http.post<SingleAnalysis>(`${this.apiBase}/api/analyze`, { sourceId, css });
  }

  recommendForBundledPortfolio(): Observable<AiRecommendationResponse> {
    return this.http.get<AiRecommendationResponse>(`${this.apiBase}/api/portfolio/ai-recommendations`);
  }

  recommendForPortfolio(input: unknown): Observable<AiRecommendationResponse> {
    return this.http.post<AiRecommendationResponse>(`${this.apiBase}/api/portfolio/ai-recommendations`, input);
  }

  generateMigrationBundle(request: unknown): Observable<CssMigrationBundle> {
    return this.http.post<CssMigrationBundle>(`${this.apiBase}/api/portfolio/migration-bundle`, request);
  }

  loadThemePresets(): Observable<ThemePreset[]> {
    return this.http.get<ThemePreset[]>(`${this.apiBase}/api/theme/presets`);
  }

  compileTheme(configuration: TenantThemeConfiguration): Observable<ThemeCompilationResult> {
    return this.http.post<ThemeCompilationResult>(`${this.apiBase}/api/theme/compile`, configuration);
  }

  projectThemeFromMigration(request: unknown): Observable<MigratedThemeProjectionResult> {
    return this.http.post<MigratedThemeProjectionResult>(`${this.apiBase}/api/theme/project-from-migration`, request);
  }
}
