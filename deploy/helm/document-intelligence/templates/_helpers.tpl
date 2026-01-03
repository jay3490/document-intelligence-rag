{{- define "di.name" -}}
{{- .Chart.Name | trunc 63 | trimSuffix "-" -}}
{{- end -}}

{{- define "di.fullname" -}}
{{- if contains .Chart.Name .Release.Name -}}
{{- .Release.Name | trunc 63 | trimSuffix "-" -}}
{{- else -}}
{{- printf "%s-%s" .Release.Name .Chart.Name | trunc 63 | trimSuffix "-" -}}
{{- end -}}
{{- end -}}

{{- define "di.labels" -}}
helm.sh/chart: {{ printf "%s-%s" .Chart.Name .Chart.Version | replace "+" "_" }}
app.kubernetes.io/name: {{ include "di.name" . }}
app.kubernetes.io/instance: {{ .Release.Name }}
app.kubernetes.io/version: {{ .Chart.AppVersion | quote }}
app.kubernetes.io/managed-by: {{ .Release.Service }}
{{- end -}}

{{/* Selector labels for one component: include "di.selector" (list . "api") */}}
{{- define "di.selector" -}}
{{- $root := index . 0 -}}
app.kubernetes.io/name: {{ include "di.name" $root }}
app.kubernetes.io/instance: {{ $root.Release.Name }}
app.kubernetes.io/component: {{ index . 1 }}
{{- end -}}

{{/* Image reference: include "di.image" (list . .Values.image.api) */}}
{{- define "di.image" -}}
{{- $root := index . 0 -}}
{{- $image := index . 1 -}}
{{- $tag := default $root.Chart.AppVersion $image.tag -}}
{{- if $root.Values.image.registry -}}
{{- printf "%s/%s:%s" $root.Values.image.registry $image.repository $tag -}}
{{- else -}}
{{- printf "%s:%s" $image.repository $tag -}}
{{- end -}}
{{- end -}}

{{/* Optional environment variable from the existing Secret. */}}
{{- define "di.secretEnv" -}}
{{- $root := index . 0 -}}
- name: {{ index . 1 }}
  valueFrom:
    secretKeyRef:
      name: {{ $root.Values.secrets.existingSecret }}
      key: {{ index . 2 }}
      optional: true
{{- end -}}

{{/* Secrets shared by the API and the worker. */}}
{{- define "di.sharedSecretEnv" -}}
{{- $keys := .Values.secrets.keys -}}
{{ include "di.secretEnv" (list . "Persistence__ConnectionString" $keys.postgresConnectionString) }}
{{ include "di.secretEnv" (list . "QueryCache__RedisConnection" $keys.redisConnection) }}
{{ include "di.secretEnv" (list . "Ai__ApiKey" $keys.aiApiKey) }}
{{ include "di.secretEnv" (list . "VectorStore__Qdrant__ApiKey" $keys.qdrantApiKey) }}
{{ include "di.secretEnv" (list . "Kafka__SaslPassword" $keys.kafkaSaslPassword) }}
{{ include "di.secretEnv" (list . "BlobStorage__AzureBlob__ConnectionString" $keys.azureBlobConnectionString) }}
{{- end -}}

{{- define "di.metricsAnnotations" -}}
{{- if .Values.metrics.podAnnotations }}
prometheus.io/scrape: "true"
prometheus.io/path: /metrics
prometheus.io/port: "8080"
{{- end }}
{{- end -}}

{{/* Probes against the shared health endpoints on port 8080. */}}
{{- define "di.probes" -}}
livenessProbe:
  httpGet:
    path: /health/live
    port: http
  {{- toYaml .Values.probes.liveness | nindent 2 }}
readinessProbe:
  httpGet:
    path: /health/ready
    port: http
  {{- toYaml .Values.probes.readiness | nindent 2 }}
{{- end -}}
