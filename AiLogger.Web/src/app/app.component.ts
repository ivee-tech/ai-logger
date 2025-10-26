import { Component, ElementRef, ViewChild, inject } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormBuilder, ReactiveFormsModule } from '@angular/forms';
import { HttpErrorResponse } from '@angular/common/http';
import { finalize } from 'rxjs/operators';

import { LogAnalysisService } from './services/log-analysis.service';
import { LogAnalysisRequest, LogAnalysisResponse, TokenUsage } from './models/log-analysis.models';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, ReactiveFormsModule],
  templateUrl: './app.component.html',
  styleUrl: './app.component.scss'
})
export class AppComponent {
  private readonly fb = inject(FormBuilder);
  private readonly logAnalysisService = inject(LogAnalysisService);

  @ViewChild('fileInput')
  private fileInput?: ElementRef<HTMLInputElement>;

  readonly title = 'AI Log Sanitizer';

  readonly uploadForm = this.fb.nonNullable.group({
    provider: ['auto'],
    notes: ['']
  });

  readonly providerOptions = [
    { value: 'auto', label: 'Auto select (recommended)' },
    { value: 'AzureOpenAI', label: 'Azure OpenAI' },
    { value: 'OpenAI', label: 'OpenAI' },
    { value: 'GitHub', label: 'GitHub Models' },
    { value: 'Ollama', label: 'Ollama (local)' }
  ];

  selectedFile: File | null = null;
  isDragOver = false;
  isSubmitting = false;
  errorMessage = '';
  analysisResult: LogAnalysisResponse | null = null;

  get canSubmit(): boolean {
    return !!this.selectedFile && !this.isSubmitting;
  }

  get selectedFileSummary(): string {
    return this.selectedFile
      ? `${this.selectedFile.name} (${this.formatBytes(this.selectedFile.size)})`
      : 'No file selected';
  }

  triggerFileDialog(): void {
    if (this.isSubmitting) {
      return;
    }

    this.fileInput?.nativeElement.click();
  }

  onFileInputChange(event: Event): void {
    const target = event.target as HTMLInputElement;
    const file = target.files?.item(0) ?? null;
    this.setFile(file);
    target.value = '';
  }

  handleDragOver(event: DragEvent): void {
    event.preventDefault();
    if (!this.isSubmitting) {
      this.isDragOver = true;
    }
  }

  handleDragLeave(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver = false;
  }

  handleDrop(event: DragEvent): void {
    event.preventDefault();
    this.isDragOver = false;
    const file = event.dataTransfer?.files.item(0) ?? null;
    this.setFile(file);
  }

  removeFile(): void {
    this.setFile(null);
  }

  downloadSanitizedCopy(): void {
    if (!this.analysisResult) {
      return;
    }

    const blob = new Blob([this.analysisResult.sanitizedContent], { type: 'text/plain;charset=utf-8' });
  const link = document.createElement('a');
  link.download = this.deriveSanitizedFileName();
  link.href = URL.createObjectURL(blob);
  document.body.append(link);
  // One-off link; click then dispose to prevent leaks.
  link.click();
  document.body.removeChild(link);
  URL.revokeObjectURL(link.href);
  }

  submitAnalysis(): void {
    if (!this.selectedFile || this.isSubmitting) {
      this.errorMessage = 'Select a log file before submitting it for analysis.';
      return;
    }

    const request = this.buildRequest();
    this.analysisResult = null;
    this.errorMessage = '';
    this.isSubmitting = true;

    this.logAnalysisService
      .analyzeLog(this.selectedFile, request)
      .pipe(finalize(() => (this.isSubmitting = false)))
      .subscribe({
        next: result => (this.analysisResult = result),
        error: error => (this.errorMessage = this.resolveErrorMessage(error))
      });
  }

  resetForm(): void {
    this.uploadForm.reset({ provider: 'auto', notes: '' });
    this.selectedFile = null;
    this.errorMessage = '';
    this.analysisResult = null;
    this.isDragOver = false;
    this.clearFileInput();
  }

  get hasSensitiveFindings(): boolean {
    return !!this.analysisResult?.sensitiveFindings?.length;
  }

  tokenTotal(usage?: TokenUsage | null): number | null {
    if (!usage) {
      return null;
    }

    return usage.totalTokens ?? usage.promptTokens + usage.completionTokens;
  }

  private buildRequest(): LogAnalysisRequest {
    const { provider, notes } = this.uploadForm.getRawValue();
    return {
      provider: provider === 'auto' ? undefined : provider,
      notes: notes?.trim() ?? undefined
    };
  }

  private setFile(file: File | null): void {
    if (file) {
      this.selectedFile = file;
      this.errorMessage = '';
    } else {
      this.selectedFile = null;
    }
    this.clearFileInput();
    this.analysisResult = null;
  }

  private clearFileInput(): void {
    if (this.fileInput) {
      this.fileInput.nativeElement.value = '';
    }
  }

  private resolveErrorMessage(error: unknown): string {
    if (error instanceof HttpErrorResponse) {
      if (typeof error.error === 'string' && error.error.trim().length > 0) {
        return error.error;
      }
      if (error.error?.message) {
        return error.error.message;
      }
      return `The analysis request failed with status ${error.status}.`;
    }
    return 'Something went wrong while analyzing the log file.';
  }

  private formatBytes(bytes: number): string {
    if (!Number.isFinite(bytes) || bytes <= 0) {
      return '0 B';
    }

    const units = ['B', 'KB', 'MB', 'GB'];
    const index = Math.min(Math.floor(Math.log(bytes) / Math.log(1024)), units.length - 1);
    const value = bytes / Math.pow(1024, index);
    return `${value.toFixed(index === 0 ? 0 : 1)} ${units[index]}`;
  }

  private deriveSanitizedFileName(): string {
    const original = this.analysisResult?.metadata.originalFileName?.trim() || 'log.txt';
    const lastDot = original.lastIndexOf('.');
    const base = lastDot > 0 ? original.slice(0, lastDot) : original;
    const extension = lastDot > 0 ? original.slice(lastDot) : '.txt';
    return `${base}.sanitized${extension}`;
  }
}
