using DocAgent.Utils;
using DocAgent.AI;
using DocAgent.Agents;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace DocAgent.Services
{
public class AgentOrchestrator
{
    private const int MaxFileBytes = 200_000;
    private static readonly string[] LogicExtensions =
    [
        ".cs", ".js", ".ts", ".tsx", ".jsx", ".java", ".py", ".go", ".rb", ".php",
        ".kt", ".swift", ".rs", ".cpp", ".c", ".h"
    ];

    public async Task Run(string repoPath, string[] changedFiles, string outputDir)
    {
        var resolvedRepoPath = repoPath;

        // ✅ If remote repo → clone
        if (repoPath.StartsWith("http", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("📦 Cloning repository...");
            resolvedRepoPath = GitHelper.CloneRepo(repoPath);
        }

        // ✅ Get changed files dynamically
        var fileContents = new List<string>();
        var fileContentByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var fileRawContentByPath = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // ✅ If no files → fallback to full repo (optional)
        if (changedFiles == null || changedFiles.Length == 0)
        {
            Console.WriteLine("⚠️ No changed files provided. Detecting changed files...");

            // var allFiles = Directory.GetFiles(resolvedRepoPath, "*.*", SearchOption.AllDirectories);
            var allFiles = GitHelper.GetChangedFiles(resolvedRepoPath);

            // GitHelper returns repo-relative paths already.
            changedFiles = allFiles.ToArray();
            Console.WriteLine($"🔍 Detected {changedFiles.Length} changed files.");
        }

        // Normalize incoming changed-file paths from CI/git sources.
        changedFiles = changedFiles
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Select(f => f.Replace('\\', '/').Trim())
            .Select(f => f.StartsWith("./", StringComparison.Ordinal) ? f[2..] : f)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // ✅ Read only changed files
        foreach (var file in changedFiles)
        {
            var normalizedFile = file.Replace('/', Path.DirectorySeparatorChar);
            var fullPath = Path.Combine(resolvedRepoPath, normalizedFile);

            if (File.Exists(fullPath))
            {
                try
                {
                    var fileInfo = new FileInfo(fullPath);
                    if (fileInfo.Length > MaxFileBytes)
                    {
                        Console.WriteLine($"⚠️ Skipping large file: {file} ({fileInfo.Length} bytes)");
                        continue;
                    }

                    var content = File.ReadAllText(fullPath);
                    if (LooksBinary(content))
                    {
                        Console.WriteLine($"⚠️ Skipping binary-like file: {file}");
                        continue;
                    }

                    var blob = $"FILE: {file}\n{content}";
                    fileContents.Add(blob);
                    fileContentByPath[file] = blob;
                    fileRawContentByPath[file] = content;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"⚠️ Skipping file: {file}");
                    Console.WriteLine(ex.Message);
                }
            }
        }

        var ai = AIProviderFactory.Create();

        var planner = new PlannerAgent(ai);
        var analyzer = new AnalyzerAgent(ai);
        var writer = new WriterAgent(ai);
        var feedback = new FeedbackAgent(ai);

        Console.WriteLine("🧭 Running PlannerAgent...");
        var plan = await planner.Plan(string.Join("\n", changedFiles));

        var plannedFiles = ExtractSelectedFiles(plan);
        var selectedForAnalysis = plannedFiles
            .Where(fileContentByPath.ContainsKey)
            .Select(f => fileContentByPath[f])
            .ToList();

        if (selectedForAnalysis.Count == 0)
        {
            selectedForAnalysis = fileContents;
        }

        Console.WriteLine("🔎 Running AnalyzerAgent...");
        var analysis = await analyzer.Analyze(string.Join("\n\n", selectedForAnalysis));

        var writerInput =
            "Requested document types:\nREADME.md\narchitecture.md\napi.md\nfrontend.md\nwalkthrough.md\n\n" +
            $"Plan:\n{plan}\n\nAnalysis:\n{analysis}\n\nFiles:\n{string.Join("\n", changedFiles)}";

        var resolvedOutputDir = string.IsNullOrWhiteSpace(outputDir)
            ? Path.Combine(resolvedRepoPath, "docs")
            : outputDir;
        var feedbackFilePath = Path.Combine(resolvedOutputDir, "feedback.md");

        Console.WriteLine("✍️ Running WriterAgent...");
        var writerPackJson = await writer.Generate(writerInput);
        var writerPack = ExtractWriterSummaryPack(writerPackJson);
        var readmeDoc = BuildProjectReadme(plan, analysis, changedFiles, writerPack, fileRawContentByPath);
        var architectureDoc = BuildArchitectureDoc(plan, analysis, changedFiles, writerPack, fileRawContentByPath);
        var apiDoc = BuildApiDoc(plan, analysis, changedFiles, writerPack, fileRawContentByPath);
        var frontendDoc = BuildFrontendDoc(plan, analysis, changedFiles, writerPack, fileRawContentByPath);
        var walkthroughDoc = BuildWalkthroughDoc(plan, analysis, changedFiles, writerPack, fileRawContentByPath);

        Console.WriteLine("🧪 Running FeedbackAgent...");
        var feedbackInput = string.Join(
            "\n\n",
            new[]
            {
                "README.md\n" + readmeDoc,
                "architecture.md\n" + architectureDoc,
                "api.md\n" + apiDoc,
                "frontend.md\n" + frontendDoc,
                "walkthrough.md\n" + walkthroughDoc
            });
        var feedbackResult = await feedback.Refine(feedbackInput);

        Directory.CreateDirectory(resolvedOutputDir);
        var outputFile = Path.Combine(resolvedOutputDir, "README.md");
        File.WriteAllText(outputFile, readmeDoc);
        File.WriteAllText(Path.Combine(resolvedOutputDir, "architecture.md"), architectureDoc);
        File.WriteAllText(Path.Combine(resolvedOutputDir, "api.md"), apiDoc);
        File.WriteAllText(Path.Combine(resolvedOutputDir, "frontend.md"), frontendDoc);
        File.WriteAllText(Path.Combine(resolvedOutputDir, "walkthrough.md"), walkthroughDoc);

        DeleteIfExists(Path.Combine(resolvedOutputDir, "plan.md"));
        DeleteIfExists(Path.Combine(resolvedOutputDir, "analysis.md"));
        DeleteIfExists(feedbackFilePath);

        Console.WriteLine($"✅ Documentation generated at: {outputFile}");
    }

    private static bool LooksBinary(string content)
    {
        if (string.IsNullOrEmpty(content))
        {
            return false;
        }

        var sampleSize = Math.Min(content.Length, 2000);
        var nonPrintableCount = 0;

        for (var i = 0; i < sampleSize; i++)
        {
            var c = content[i];
            if (char.IsControl(c) && c is not ('\r' or '\n' or '\t'))
            {
                nonPrintableCount++;
            }
        }

        return (double)nonPrintableCount / sampleSize > 0.1;
    }

    private static void DeleteIfExists(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string BuildProjectReadme(
        string plan,
        string analysis,
        string[] changedFiles,
        WriterSummaryPack writerPack,
        Dictionary<string, string> fileRawContentByPath)
    {
        var model = BuildDocumentModel(plan, analysis, changedFiles, fileRawContentByPath);

        var output = new StringBuilder();
        output.AppendLine("# Project Overview");
        output.AppendLine();
        output.AppendLine("## Overview");
        output.AppendLine($"Generated from {model.Selected.Count} logic files out of {changedFiles.Length} changed files.");
        output.AppendLine($"Frontend files detected: {model.Frontend.Count}. Backend files detected: {model.Backend.Count}.");
        if (!string.IsNullOrWhiteSpace(writerPack.ProjectOverview))
        {
            output.AppendLine(writerPack.ProjectOverview);
        }
        if (!string.IsNullOrWhiteSpace(model.SystemFlow)
            && !model.SystemFlow.Contains("workflow", StringComparison.OrdinalIgnoreCase)
            && !model.SystemFlow.Contains("act", StringComparison.OrdinalIgnoreCase)
            && !model.SystemFlow.Contains("script", StringComparison.OrdinalIgnoreCase))
        {
            output.AppendLine(model.SystemFlow.Trim());
        }
        output.AppendLine();

        output.AppendLine("## Documentation Set");
        output.AppendLine("- architecture.md: System structure and responsibilities.");
        output.AppendLine("- api.md: Backend/API surface and operations.");
        output.AppendLine("- frontend.md: Frontend components and user-facing flows.");
        output.AppendLine("- walkthrough.md: Execution walkthrough across detected flows.");
        output.AppendLine();

        output.AppendLine("## Project Surface");
        if (model.Selected.Count == 0)
        {
            output.AppendLine("No logic files were selected by planner/analyzer.");
        }
        else
        {
            foreach (var item in model.Selected)
            {
                var reason = string.IsNullOrWhiteSpace(item.Reason) ? "Logic-relevant file." : item.Reason.Trim();
                output.AppendLine($"- {item.File}: {reason}");
            }
        }
        output.AppendLine();

        output.AppendLine("## Frontend Summary");
        if (model.Frontend.Count == 0)
        {
            output.AppendLine("No frontend-specific logic files were detected.");
        }
        else
        {
            foreach (var item in model.Frontend)
            {
                output.AppendLine($"- {item.File}");
            }
        }
        output.AppendLine();

        output.AppendLine("## Backend Summary");
        if (model.Backend.Count == 0)
        {
            output.AppendLine("No backend/API-specific logic files were detected.");
        }
        else
        {
            foreach (var item in model.Backend)
            {
                output.AppendLine($"- {item.File}");
            }
        }
        output.AppendLine();

        output.AppendLine("## Key Flows");
        if (model.Flows.Count == 0)
        {
            output.AppendLine("Detailed execution walkthroughs are captured in walkthrough.md.");
        }
        else
        {
            foreach (var flow in model.Flows.Take(5))
            {
                output.AppendLine($"- {flow.Name}: {flow.Description}");
            }
        }
        output.AppendLine();

        output.AppendLine("## Changed Files");
        foreach (var file in changedFiles)
        {
            output.AppendLine($"- {file}");
        }

        return output.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string BuildArchitectureDoc(
        string plan,
        string analysis,
        string[] changedFiles,
        WriterSummaryPack writerPack,
        Dictionary<string, string> fileRawContentByPath)
    {
        var model = BuildDocumentModel(plan, analysis, changedFiles, fileRawContentByPath);
        var output = new StringBuilder();
        output.AppendLine("# Architecture");
        output.AppendLine();
        output.AppendLine("## System Summary");
        output.AppendLine($"The analyzed change set contains {model.Selected.Count} logic files split across {model.Frontend.Count} frontend files and {model.Backend.Count} backend files.");
        if (!string.IsNullOrWhiteSpace(writerPack.ArchitectureSummary))
        {
            output.AppendLine(writerPack.ArchitectureSummary);
        }
        if (!string.IsNullOrWhiteSpace(model.SystemFlow))
        {
            output.AppendLine(model.SystemFlow);
        }
        output.AppendLine();

        output.AppendLine("## Frontend Layer");
        AppendSelectedFiles(output, model.Frontend, "No frontend layer was detected in the analyzed logic files.");
        output.AppendLine();

        output.AppendLine("## Backend Layer");
        AppendSelectedFiles(output, model.Backend, "No backend layer was detected in the analyzed logic files.");
        output.AppendLine();

        output.AppendLine("## Core Interactions");
        if (model.Flows.Count == 0)
        {
            output.AppendLine("No execution flows were detected.");
        }
        else
        {
            foreach (var flow in model.Flows)
            {
                output.AppendLine($"- {flow.Name}: {flow.Description}");
            }
        }
        output.AppendLine();

        output.AppendLine("## Changed Files");
        foreach (var file in changedFiles)
        {
            output.AppendLine($"- {file}");
        }

        return output.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string BuildApiDoc(
        string plan,
        string analysis,
        string[] changedFiles,
        WriterSummaryPack writerPack,
        Dictionary<string, string> fileRawContentByPath)
    {
        var model = BuildDocumentModel(plan, analysis, changedFiles, fileRawContentByPath);
        var output = new StringBuilder();
        output.AppendLine("# API Documentation");
        output.AppendLine();
        output.AppendLine("## Backend Surface");
        AppendSelectedFiles(output, model.Backend, "No backend/API-specific logic files were detected.");
        output.AppendLine();

        output.AppendLine("## Operations");
        if (model.Backend.Count == 0 || model.ApiFlows.Count == 0)
        {
            output.AppendLine("No backend/API operations were detected in the analyzed files.");
        }
        else
        {
            if (!string.IsNullOrWhiteSpace(writerPack.ApiSummary))
            {
                output.AppendLine(writerPack.ApiSummary);
            }
            foreach (var operation in writerPack.ApiOperations)
            {
                output.AppendLine($"- {operation}");
            }
            foreach (var flow in model.ApiFlows)
            {
                output.AppendLine($"- {flow.Name}: {flow.Description}");
            }
        }
        output.AppendLine();

        output.AppendLine("## Notes");
        output.AppendLine("This document focuses on backend/API behavior only.");

        return output.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string BuildFrontendDoc(
        string plan,
        string analysis,
        string[] changedFiles,
        WriterSummaryPack writerPack,
        Dictionary<string, string> fileRawContentByPath)
    {
        var model = BuildDocumentModel(plan, analysis, changedFiles, fileRawContentByPath);
        var output = new StringBuilder();
        output.AppendLine("# Frontend Documentation");
        output.AppendLine();
        output.AppendLine("## Frontend Surface");
        AppendSelectedFiles(output, model.Frontend, "No frontend-specific logic files were detected.");
        output.AppendLine();

        if (!string.IsNullOrWhiteSpace(writerPack.FrontendSummary))
        {
            output.AppendLine("## Frontend Summary");
            output.AppendLine(writerPack.FrontendSummary);
            output.AppendLine();
        }

        output.AppendLine("## User-Facing Flows");
        var frontendFlows = model.Frontend.Count == 0
            ? new List<ExecutionFlow>()
            : model.Flows;
        if (frontendFlows.Count == 0)
        {
            output.AppendLine("No frontend interaction flows were detected.");
        }
        else
        {
            foreach (var component in writerPack.FrontendComponents)
            {
                output.AppendLine($"- {component}");
            }
            foreach (var flow in frontendFlows)
            {
                output.AppendLine($"- {flow.Name}: {flow.Description}");
            }
        }
        output.AppendLine();

        output.AppendLine("## Component Interaction");
        if (model.Frontend.Count == 0)
        {
            output.AppendLine("No frontend component interactions were detected.");
        }
        else
        {
            foreach (var item in model.Frontend)
            {
                var category = string.IsNullOrWhiteSpace(item.Category) ? "Frontend" : item.Category;
                output.AppendLine($"- {item.File} ({category}): {item.Reason}");
            }
        }

        return output.ToString().TrimEnd() + Environment.NewLine;
    }

    private static string BuildWalkthroughDoc(
        string plan,
        string analysis,
        string[] changedFiles,
        WriterSummaryPack writerPack,
        Dictionary<string, string> fileRawContentByPath)
    {
        var model = BuildDocumentModel(plan, analysis, changedFiles, fileRawContentByPath);
        var output = new StringBuilder();
        output.AppendLine("# Walkthrough");
        output.AppendLine();
        if (!string.IsNullOrWhiteSpace(writerPack.WalkthroughSummary))
        {
            output.AppendLine("## Summary");
            output.AppendLine(writerPack.WalkthroughSummary);
            output.AppendLine();
        }
        output.AppendLine("## Flow Sequence");
        if (model.Flows.Count == 0)
        {
            output.AppendLine("No execution walkthrough was detected.");
        }
        else
        {
            for (var i = 0; i < writerPack.WalkthroughSteps.Count; i++)
            {
                output.AppendLine($"{i + 1}. {writerPack.WalkthroughSteps[i]}");
            }
            for (var i = 0; i < model.Flows.Count; i++)
            {
                var flow = model.Flows[i];
                output.AppendLine($"{i + 1 + writerPack.WalkthroughSteps.Count}. {flow.Name}: {flow.Description}");
            }
        }
        output.AppendLine();

        output.AppendLine("## Relevant Logic Files");
        AppendSelectedFiles(output, model.Selected, "No logic files were selected.");

        return output.ToString().TrimEnd() + Environment.NewLine;
    }

    private static DocumentModel BuildDocumentModel(
        string plan,
        string analysis,
        string[] changedFiles,
        Dictionary<string, string> fileRawContentByPath)
    {
        var selected = ExtractSelectedFileDetails(plan)
            .Where(s => IsLogicFilePath(s.File))
            .ToList();
        var logicChanged = changedFiles.Where(IsLogicFilePath).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var flows = ExtractExecutionFlows(analysis);
        var systemFlow = ExtractSystemFlow(analysis);

        if (selected.Count == 0)
        {
            selected = logicChanged
                .Select(path => new SelectedFile(path, "Logic", "Detected as a source-code change."))
                .ToList();
        }

        if (flows.Count == 0)
        {
            flows = ExtractFlowsFromSource(selected, fileRawContentByPath);
        }

        var frontend = selected.Where(s => IsFrontendFilePath(s.File, fileRawContentByPath)).ToList();
        var backend = selected.Where(s => IsBackendFilePath(s.File, fileRawContentByPath)).ToList();
        var apiFlows = flows.Where(IsApiLikeFlow).ToList();

        if (backend.Count == 0)
        {
            apiFlows = new List<ExecutionFlow>();
        }

        return new DocumentModel(selected, frontend, backend, flows, apiFlows, systemFlow);
    }

    private static string NormalizeNewlines(string text)
    {
        return (text ?? string.Empty).Replace("\r\n", "\n").Replace('\r', '\n');
    }

    private static bool IsLogicFilePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (path.StartsWith(".github/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("docs/", StringComparison.OrdinalIgnoreCase)
            || path.StartsWith("scripts/", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".gitignore", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".md", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".yml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".yaml", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var ext = Path.GetExtension(path);
        return LogicExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsFrontendFilePath(string path, Dictionary<string, string> fileRawContentByPath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (HasBackendHandlerSource(path, fileRawContentByPath))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        return normalized.Contains("/mfe", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/frontend/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/client/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/web/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/ui/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/component", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/page", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/view", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/app/", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".tsx", StringComparison.OrdinalIgnoreCase)
            || normalized.EndsWith(".jsx", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBackendFilePath(string path, Dictionary<string, string> fileRawContentByPath)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        if (IsFrontendFilePath(path, fileRawContentByPath))
        {
            return false;
        }

        var normalized = path.Replace('\\', '/');
        var ext = Path.GetExtension(normalized);
        var pathSignals = normalized.Contains("/backend/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/server/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/api/", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/controller", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/route", StringComparison.OrdinalIgnoreCase)
            || normalized.Contains("/handler", StringComparison.OrdinalIgnoreCase);
        var languageSignals = ext.Equals(".cs", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".java", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".go", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".py", StringComparison.OrdinalIgnoreCase);

        return HasBackendHandlerSource(path, fileRawContentByPath)
            && (pathSignals || languageSignals);
    }

    private static bool HasBackendHandlerSource(string path, Dictionary<string, string> fileRawContentByPath)
    {
        if (!fileRawContentByPath.TryGetValue(path, out var source) || string.IsNullOrWhiteSpace(source))
        {
            return false;
        }

        return source.Contains("[HttpGet", StringComparison.OrdinalIgnoreCase)
            || source.Contains("[HttpPost", StringComparison.OrdinalIgnoreCase)
            || source.Contains("[HttpPut", StringComparison.OrdinalIgnoreCase)
            || source.Contains("[HttpDelete", StringComparison.OrdinalIgnoreCase)
            || source.Contains("MapGet(", StringComparison.OrdinalIgnoreCase)
            || source.Contains("MapPost(", StringComparison.OrdinalIgnoreCase)
            || source.Contains("MapPut(", StringComparison.OrdinalIgnoreCase)
            || source.Contains("MapDelete(", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(source, @"\b(router|app)\.(get|post|put|patch|delete)\s*\(", RegexOptions.IgnoreCase)
            || Regex.IsMatch(source, @"@(?:app|router)\.(get|post|put|patch|delete|route)\b", RegexOptions.IgnoreCase)
            || source.Contains("ControllerBase", StringComparison.OrdinalIgnoreCase)
            || source.Contains("HttpRequest", StringComparison.OrdinalIgnoreCase)
            || source.Contains("HttpResponse", StringComparison.OrdinalIgnoreCase)
            || Regex.IsMatch(source, @"\b(req|request)\b.*\b(res|response)\b", RegexOptions.IgnoreCase);
    }

    private static bool IsApiLikeFlow(ExecutionFlow flow)
    {
        return flow.Name.Contains("api", StringComparison.OrdinalIgnoreCase)
            || flow.Name.Contains("request", StringComparison.OrdinalIgnoreCase)
            || flow.Name.Contains("create", StringComparison.OrdinalIgnoreCase)
            || flow.Name.Contains("update", StringComparison.OrdinalIgnoreCase)
            || flow.Name.Contains("delete", StringComparison.OrdinalIgnoreCase)
            || flow.Name.Contains("get", StringComparison.OrdinalIgnoreCase)
            || flow.Name.Contains("post", StringComparison.OrdinalIgnoreCase)
            || flow.Name.Contains("put", StringComparison.OrdinalIgnoreCase)
            || flow.Name.Contains("patch", StringComparison.OrdinalIgnoreCase)
            || flow.Description.Contains("request", StringComparison.OrdinalIgnoreCase)
            || flow.Description.Contains("response", StringComparison.OrdinalIgnoreCase);
    }

    private static void AppendSelectedFiles(StringBuilder output, List<SelectedFile> files, string fallback)
    {
        if (files.Count == 0)
        {
            output.AppendLine(fallback);
            return;
        }

        foreach (var item in files)
        {
            var reason = string.IsNullOrWhiteSpace(item.Reason) ? "Selected as logic-relevant." : item.Reason;
            output.AppendLine($"- {item.File}: {reason}");
        }
    }

    private static WriterSummaryPack ExtractWriterSummaryPack(string writerPackJson)
    {
        if (!TryParseJson(writerPackJson, out var root))
        {
            return WriterSummaryPack.Empty;
        }

        return new WriterSummaryPack(
            GetString(root, "project_overview"),
            GetString(root, "architecture_summary"),
            GetString(root, "api_summary"),
            GetString(root, "frontend_summary"),
            GetString(root, "walkthrough_summary"),
            GetStringArray(root, "api_operations"),
            GetStringArray(root, "frontend_components"),
            GetStringArray(root, "walkthrough_steps"));
    }

    private static string GetString(JsonElement root, string propertyName)
    {
        return root.TryGetProperty(propertyName, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;
    }

    private static List<string> GetStringArray(JsonElement root, string propertyName)
    {
        var result = new List<string>();
        if (!root.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in value.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    result.Add(text.Trim());
                }
            }
        }

        return result;
    }

    private static List<string> ExtractSelectedFiles(string plan)
    {
        return ExtractSelectedFileDetails(plan)
            .Select(s => s.File)
            .Where(f => !string.IsNullOrWhiteSpace(f))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static List<SelectedFile> ExtractSelectedFileDetails(string plan)
    {
        var result = new List<SelectedFile>();
        if (!TryParseJson(plan, out var root))
        {
            return result;
        }

        if (!root.TryGetProperty("selected_files", out var selectedFiles)
            || selectedFiles.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in selectedFiles.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var file = item.TryGetProperty("file", out var fileProp)
                ? fileProp.GetString() ?? string.Empty
                : string.Empty;
            var category = item.TryGetProperty("category", out var categoryProp)
                ? categoryProp.GetString() ?? string.Empty
                : string.Empty;
            var reason = item.TryGetProperty("reason", out var reasonProp)
                ? reasonProp.GetString() ?? string.Empty
                : string.Empty;

            if (!string.IsNullOrWhiteSpace(file))
            {
                result.Add(new SelectedFile(file.Trim(), category.Trim(), reason.Trim()));
            }
        }

        return result;
    }

    private static List<ExecutionFlow> ExtractExecutionFlows(string analysis)
    {
        var flows = new List<ExecutionFlow>();
        if (!TryParseJson(analysis, out var root))
        {
            return flows;
        }

        if (!root.TryGetProperty("execution_flows", out var executionFlows)
            || executionFlows.ValueKind != JsonValueKind.Array)
        {
            return flows;
        }

        foreach (var flow in executionFlows.EnumerateArray())
        {
            if (flow.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            var name = flow.TryGetProperty("name", out var nameProp)
                ? (nameProp.GetString() ?? "Flow")
                : "Flow";

            var description = flow.TryGetProperty("description", out var descProp)
                ? (descProp.GetString() ?? string.Empty)
                : string.Empty;

            if (string.IsNullOrWhiteSpace(description))
            {
                var inputsText = ExtractArrayAsCsv(flow, "inputs");
                var outputsText = ExtractArrayAsCsv(flow, "outputs");
                var parts = new List<string>();
                if (!string.IsNullOrWhiteSpace(inputsText))
                {
                    parts.Add($"inputs: {inputsText}");
                }

                if (!string.IsNullOrWhiteSpace(outputsText))
                {
                    parts.Add($"outputs: {outputsText}");
                }

                if (parts.Count > 0)
                {
                    description = string.Join("; ", parts);
                }
            }

            if (string.IsNullOrWhiteSpace(description)
                && flow.TryGetProperty("steps", out var stepsProp)
                && stepsProp.ValueKind == JsonValueKind.Array)
            {
                var steps = new List<string>();
                foreach (var step in stepsProp.EnumerateArray())
                {
                    if (step.ValueKind == JsonValueKind.Object
                        && step.TryGetProperty("action", out var actionProp)
                        && !string.IsNullOrWhiteSpace(actionProp.GetString()))
                    {
                        steps.Add(actionProp.GetString()!);
                    }
                    else if (step.ValueKind == JsonValueKind.String)
                    {
                        var value = step.GetString();
                        if (!string.IsNullOrWhiteSpace(value))
                        {
                            steps.Add(value);
                        }
                    }
                }

                description = steps.Count > 0
                    ? string.Join(" -> ", steps)
                    : "Flow details are available in analysis.md.";
            }

            flows.Add(new ExecutionFlow(name.Trim(), description.Trim()));
        }

        return flows;
    }

    private static string ExtractArrayAsCsv(JsonElement obj, string propertyName)
    {
        if (!obj.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Array)
        {
            return string.Empty;
        }

        var values = new List<string>();
        foreach (var item in prop.EnumerateArray())
        {
            if (item.ValueKind == JsonValueKind.String)
            {
                var text = item.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    values.Add(text.Trim());
                }
            }
        }

        return string.Join(", ", values);
    }

    private static List<ExecutionFlow> ExtractFlowsFromSource(
        List<SelectedFile> selected,
        Dictionary<string, string> fileRawContentByPath)
    {
        var flows = new List<ExecutionFlow>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var item in selected)
        {
            if (!fileRawContentByPath.TryGetValue(item.File, out var source)
                || string.IsNullOrWhiteSpace(source))
            {
                continue;
            }

            var regexes = new[]
            {
                new Regex(@"function\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)", RegexOptions.Compiled),
                new Regex(@"(?:export\s+)?const\s+([A-Za-z_][A-Za-z0-9_]*)\s*=\s*\(([^)]*)\)\s*=>", RegexOptions.Compiled),
                new Regex(@"(?:public|private|protected)?\s*(?:async\s+)?[A-Za-z0-9_<>,\[\]\?]+\s+([A-Za-z_][A-Za-z0-9_]*)\s*\(([^)]*)\)", RegexOptions.Compiled)
            };

            foreach (var regex in regexes)
            {
                foreach (Match match in regex.Matches(source))
                {
                    if (match.Groups.Count < 2)
                    {
                        continue;
                    }

                    var method = match.Groups[1].Value.Trim();
                    if (string.IsNullOrWhiteSpace(method) || !seen.Add($"{item.File}:{method}"))
                    {
                        continue;
                    }

                    var args = match.Groups.Count > 2
                        ? match.Groups[2].Value.Trim()
                        : string.Empty;

                    var description = string.IsNullOrWhiteSpace(args)
                        ? $"Defined in {item.File}"
                        : $"inputs: {args} (defined in {item.File})";

                    flows.Add(new ExecutionFlow(method, description));
                }
            }
        }

        return flows;
    }

    private static string ExtractSystemFlow(string analysis)
    {
        if (!TryParseJson(analysis, out var root))
        {
            return string.Empty;
        }

        return root.TryGetProperty("system_flow", out var systemFlow)
            ? (systemFlow.GetString() ?? string.Empty)
            : string.Empty;
    }

    private static bool TryParseJson(string input, out JsonElement root)
    {
        root = default;

        var block = ExtractJsonBlock(input);
        if (string.IsNullOrWhiteSpace(block))
        {
            return false;
        }

        try
        {
            using var doc = JsonDocument.Parse(block);
            root = doc.RootElement.Clone();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string ExtractJsonBlock(string input)
    {
        var text = NormalizeNewlines(input);
        var start = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (start < 0 || end <= start)
        {
            return string.Empty;
        }

        return text[start..(end + 1)];
    }

    private static bool IsNoiseLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line))
        {
            return true;
        }

        return line.Contains("based on the provided input", StringComparison.OrdinalIgnoreCase)
            || line.Contains("based on the input", StringComparison.OrdinalIgnoreCase)
            || line.Contains("here is the output", StringComparison.OrdinalIgnoreCase)
            || line.Contains("here's the output", StringComparison.OrdinalIgnoreCase)
            || line.Contains("analyzer output", StringComparison.OrdinalIgnoreCase)
            || line.Contains("plan output", StringComparison.OrdinalIgnoreCase)
            || line.Contains("changed file list", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Here is", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Here's", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("I've received", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Now that I've", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("I'm ready", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Please provide", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Remember,", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("Let me know", StringComparison.OrdinalIgnoreCase)
            || line.StartsWith("That's it", StringComparison.OrdinalIgnoreCase);
    }

    private sealed record SelectedFile(string File, string Category, string Reason);
    private sealed record ExecutionFlow(string Name, string Description);
    private sealed record DocumentModel(
        List<SelectedFile> Selected,
        List<SelectedFile> Frontend,
        List<SelectedFile> Backend,
        List<ExecutionFlow> Flows,
        List<ExecutionFlow> ApiFlows,
        string SystemFlow);
    private sealed record WriterSummaryPack(
        string ProjectOverview,
        string ArchitectureSummary,
        string ApiSummary,
        string FrontendSummary,
        string WalkthroughSummary,
        List<string> ApiOperations,
        List<string> FrontendComponents,
        List<string> WalkthroughSteps)
    {
        public static WriterSummaryPack Empty => new(
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            string.Empty,
            new List<string>(),
            new List<string>(),
            new List<string>());
    }

}

}
