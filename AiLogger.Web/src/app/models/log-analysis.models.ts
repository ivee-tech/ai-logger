export interface LogAnalysisRequest {
  provider?: string;
  notes?: string;
}

export interface TokenUsage {
  promptTokens: number;
  completionTokens: number;
  totalTokens?: number;
}

export interface AnalysisMetadata {
  originalFileName: string;
  provider: string;
  processedAt: string;
  durationMs?: number;
  tokenUsage?: TokenUsage;
}

export interface SensitiveFinding {
  id?: string;
  category: string;
  description?: string;
  sample?: string;
  replacementKey?: string;
  occurrences?: number;
  confidence?: number;
}

export interface LogAnalysisResponse {
  sanitizedContent: string;
  sensitiveFindings: SensitiveFinding[];
  metadata: AnalysisMetadata;
  warnings?: string[];
}
