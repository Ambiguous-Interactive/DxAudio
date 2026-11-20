/* Apply the pinned upstream compiler safety checks to the DxAudio source and project layout. */

const { execFileSync } = require("node:child_process");
const { existsSync, readFileSync, realpathSync, writeFileSync } = require("node:fs");
const path = require("node:path");

const [, , upstream, root, report] = process.argv;
const policy = require(path.resolve(upstream, "scripts/lint-unsafe-code.js"));
const files = execFileSync("git", ["ls-files", "--cached", "--others", "--exclude-standard", "-z", "--", "Runtime", "Editor", "Tests", "Samples~", ".net-tests"], {
    cwd: root,
    encoding: "utf8",
})
    .split("\0")
    .filter((name) => name && existsSync(path.join(root, name)));
const load = (extensions) =>
    files
        .filter((name) => extensions.includes(path.extname(name)))
        .map((name) => {
            const input = path.join(root, name);
            if (realpathSync(input) !== path.join(realpathSync(root), name)) {
                throw new Error(`Safety input is linked: ${name}`);
            }
            return { path: name, text: readFileSync(input, "utf8") };
        });
const sources = load([".cs"]);
const assemblies = load([".asmdef"]);
const projects = load([".csproj", ".props", ".targets"]);
const findings = policy.findViolations(assemblies, sources, projects);
const unsafeAccess = policy.findUnsafeAccess(sources);
findings.push(...unsafeAccess.failures);

if (!assemblies.some((assembly) => assembly.path === "Runtime/WallstopStudios.DxAudio.asmdef") || !projects.some((project) => project.path === ".net-tests/DxAudio.Tests.csproj")) {
    findings.push("Expected runtime asmdef or .NET check project missing from the unsafe-code source census.");
}

const controls = [
    ["compiler switch", policy.findViolations([{ path: "Control.asmdef", text: '{"allowUnsafeCode":true}' }], [], [])],
    ["unsafe keyword", policy.findViolations([], [{ path: "Control.cs", text: "unsafe class Control {}" }], [])],
    ["IL2CPP checks", policy.findIl2CppSafetyDisables([{ path: "Control.cs", text: "[Il2CppSetOption(Option.NullChecks, false)] class Control {}" }]).failures],
    ["unbounded stack", policy.findStackAllocations([{ path: "Control.cs", text: "class Control { void M(int n) { System.Span<int> a = stackalloc int[n]; } }" }]).failures],
    ["unchecked memory", policy.findUnsafeAccess([{ path: "Control.cs", text: "class Control { void M(ref int x) { Unsafe.As<int, long>(ref x); } }" }]).failures],
];
for (const [label, diagnostics] of controls) {
    if (diagnostics.length !== 1) {
        findings.push(`The ${label} control expected one finding; got ${diagnostics.length}.`);
    }
}
const clean = { path: "Control.cs", text: "class Control { void M() { System.Span<int> a = stackalloc int[8]; } }" };
if (policy.findViolations([], [clean], []).length !== 0) {
    findings.push("The bounded stack allocation control unexpectedly failed.");
}

const subjects = {
    sourceFiles: sources.length,
    assemblyDefinitions: assemblies.length,
    checkProjects: projects.length,
    unsafeAccessSites: unsafeAccess.inspected,
};
const reportControl = policy.createSarif({ failures: ["Tests/Control.cs:2:unsafe control"], subjects });
const control = reportControl.runs[0].results[0];
if (control?.locations?.[0].physicalLocation.artifactLocation.uri !== "Tests/Control.cs" || control.locations[0].physicalLocation.region.startLine !== 2) {
    findings.push("The SARIF control failed to report its relative source location.");
}
writeFileSync(report, JSON.stringify(policy.createSarif({ failures: findings, subjects }), null, 2) + "\n");

for (const finding of findings) {
    console.error(finding);
}
if (findings.length !== 0) {
    process.exitCode = 1;
} else {
    console.log(`[upstream-unsafe] Checked ${sources.length} sources, ${assemblies.length} asmdefs, and ${projects.length} project files.`);
}
