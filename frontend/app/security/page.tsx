import type { Metadata } from 'next';
import Link from 'next/link';
import {
  ArrowLeft,
  Shield,
  Lock,
  Server,
  KeyRound,
  FileCheck2,
  AlertTriangle,
  Cpu,
  Mail,
  CheckCircle2,
} from 'lucide-react';
import Footer from '../../components/layout/Footer';

export const metadata: Metadata = {
  title: 'Security Architecture & Whitepaper — Namines',
  description: 'Threat model, multi-layered isolation, zero-SSRF introspection, and cryptographic protections of the Namines engine.',
};

export default function SecurityPage() {
  return (
    <div className="min-h-screen bg-surface-900 text-content-primary flex flex-col justify-between">
      <div className="w-full max-w-4xl mx-auto px-4 sm:px-6 lg:px-8 py-12 sm:py-20">
        {/* Back link */}
        <Link
          href="/"
          className="inline-flex items-center gap-2 text-xs font-mono text-content-muted hover:text-content-primary mb-8 transition-colors"
        >
          <ArrowLeft className="w-3.5 h-3.5" />
          <span>Back to Namines</span>
        </Link>

        {/* Header */}
        <div className="border-b border-surface-500/80 pb-8 mb-10">
          <div className="inline-flex items-center gap-2 px-3 py-1 rounded-full bg-surface-800 border border-surface-500 text-micro font-mono text-accent-text mb-4">
            <Shield className="w-3.5 h-3.5" />
            <span>Hardened Multi-Tenant Architecture</span>
          </div>
          <h1 className="text-3xl sm:text-4xl font-bold tracking-tight text-content-primary mb-3">
            Security Architecture &amp; Whitepaper
          </h1>
          <p className="text-xs font-mono text-content-subtle">
            Published: September 2026 &bull; Architecture Revision 3.1 &bull; SOC 2 Type II Audited
          </p>
        </div>

        {/* Content Body */}
        <div className="space-y-12 text-sm text-content-secondary leading-relaxed">
          {/* Section 1: Security Principles */}
          <section className="space-y-4">
            <h2 className="text-xl font-bold text-content-primary tracking-tight">
              1. Fundamental Security Invariants
            </h2>
            <p>
              Namines is designed from the protocol layer up to eliminate the security vulnerabilities common in generic cloud database tools. We operate under four non-negotiable architectural invariants:
            </p>
            <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
              <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-800/60 p-5 space-y-2">
                <div className="flex items-center gap-2 text-content-primary font-semibold text-xs">
                  <Lock className="w-4 h-4 text-accent" />
                  <span>Zero Host Socket Exposure</span>
                </div>
                <p className="text-xs text-content-muted leading-relaxed">
                  The host daemon socket (<code className="font-mono text-accent-text">docker.sock</code>) is strictly prohibited from mounting into any container. Test containers run within ephemeral, isolated subnets.
                </p>
              </div>

              <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-800/60 p-5 space-y-2">
                <div className="flex items-center gap-2 text-content-primary font-semibold text-xs">
                  <Server className="w-4 h-4 text-accent" />
                  <span>DB-Per-Project Isolation</span>
                </div>
                <p className="text-xs text-content-muted leading-relaxed">
                  Tenants do not share database schemas or tables. Each project operates on its own discrete database instance with unique cryptographic credentials.
                </p>
              </div>

              <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-800/60 p-5 space-y-2">
                <div className="flex items-center gap-2 text-content-primary font-semibold text-xs">
                  <KeyRound className="w-4 h-4 text-accent" />
                  <span>AES-256-GCM Envelope Encryption</span>
                </div>
                <p className="text-xs text-content-muted leading-relaxed">
                  All connection strings, certificates, and BYOK credentials are encrypted at rest using AES-256-GCM authenticated encryption with KMS key rotation.
                </p>
              </div>

              <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-800/60 p-5 space-y-2">
                <div className="flex items-center gap-2 text-content-primary font-semibold text-xs">
                  <FileCheck2 className="w-4 h-4 text-accent" />
                  <span>Immutable Audit Trails</span>
                </div>
                <p className="text-xs text-content-muted leading-relaxed">
                  Every proposed DDL modification, test container dry run, and approval is immutably logged with actor claims and canonical AST snapshots.
                </p>
              </div>
            </div>
          </section>

          {/* Section 2: Threat Model (STRIDE) */}
          <section className="space-y-4">
            <h2 className="text-xl font-bold text-content-primary tracking-tight">
              2. STRIDE Threat Model Analysis
            </h2>
            <div className="overflow-x-auto">
              <table className="w-full text-left text-xs font-mono border-collapse border border-surface-500">
                <thead>
                  <tr className="bg-surface-800 text-content-primary border-b border-surface-500">
                    <th className="p-3">Threat</th>
                    <th className="p-3">Attack Vector</th>
                    <th className="p-3">Architectural Countermeasure</th>
                  </tr>
                </thead>
                <tbody className="divide-y divide-surface-500/60 text-content-muted">
                  <tr>
                    <td className="p-3 font-semibold text-content-primary">Spoofing</td>
                    <td className="p-3">Forged authentication tokens</td>
                    <td className="p-3">Short-lived asymmetric JWTs stored in HttpOnly, SameSite=Strict cookies with Argon2id session hashing.</td>
                  </tr>
                  <tr>
                    <td className="p-3 font-semibold text-content-primary">Tampering</td>
                    <td className="p-3">Cross-tenant schema mutation</td>
                    <td className="p-3">Cryptographic claims verification on every request matching token organization_id to target project.</td>
                  </tr>
                  <tr>
                    <td className="p-3 font-semibold text-content-primary">Repudiation</td>
                    <td className="p-3">&ldquo;I did not apply that migration&rdquo;</td>
                    <td className="p-3">Append-only ChangeRequestAuditLog recording actor identity, IP hash, and DDL diff.</td>
                  </tr>
                  <tr>
                    <td className="p-3 font-semibold text-content-primary">Information Disclosure</td>
                    <td className="p-3">Tenant A reading Tenant B schema</td>
                    <td className="p-3">Strict private subnets, DB-per-project isolation, and gateway table-scoped API key authorization.</td>
                  </tr>
                  <tr>
                    <td className="p-3 font-semibold text-content-primary">Denial of Service</td>
                    <td className="p-3">Saturation of provisioning queues</td>
                    <td className="p-3">Per-tenant token-bucket rate limiting, execution timeouts (30s), and volatile container quotas.</td>
                  </tr>
                  <tr>
                    <td className="p-3 font-semibold text-content-primary">Privilege Elevation</td>
                    <td className="p-3">Container escape to host runner</td>
                    <td className="p-3">docker.sock elimination; native DotNet container broker running unprivileged unmapped users.</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </section>

          {/* Section 3: SSRF Protection */}
          <section id="ssrf" className="space-y-4">
            <h2 className="text-xl font-bold text-content-primary tracking-tight">
              3. Zero SSRF &amp; Private Network Guard
            </h2>
            <p>
              When users introspect external databases or test live schema diffs, Namines performs deterministic IP and DNS resolution filtering prior to establishing any TCP socket:
            </p>
            <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-800/50 p-5 space-y-2 text-xs font-mono">
              <div className="text-accent-text font-bold uppercase tracking-wider">
                Blocked Destination Ranges:
              </div>
              <ul className="list-disc pl-5 space-y-1 text-content-muted">
                <li><code className="text-content-primary">127.0.0.0/8</code> (IPv4 Loopback) and <code className="text-content-primary">::1</code> (IPv6 Loopback)</li>
                <li><code className="text-content-primary">10.0.0.0/8</code>, <code className="text-content-primary">172.16.0.0/12</code>, <code className="text-content-primary">192.168.0.0/16</code> (RFC-1918 Private Subnets)</li>
                <li><code className="text-content-primary">169.254.169.254</code> (AWS / GCP / Azure Cloud Instance Metadata Service)</li>
                <li><code className="text-content-primary">100.64.0.0/10</code> (Carrier-Grade NAT / Shared Address Space)</li>
              </ul>
            </div>
          </section>

          {/* Section 4: Gateway Guardrails */}
          <section id="gateway" className="space-y-4">
            <h2 className="text-xl font-bold text-content-primary tracking-tight">
              4. Gateway Guardrails &amp; Scoped API Keys
            </h2>
            <p>
              The Namines REST Gateway exposes deterministic interfaces for querying schema status and data. By default, all public endpoints enforce <strong className="text-content-primary font-semibold">read-only operations</strong>.
            </p>
            <p>
              Write actions (<code className="font-mono text-accent-text">create</code>, <code className="font-mono text-accent-text">update</code>, <code className="font-mono text-accent-text">delete</code>, <code className="font-mono text-accent-text">import</code>) require dedicated, cryptographically generated API keys hashed using <strong className="text-content-primary font-semibold">Argon2id</strong>. Keys are displayed exactly once at generation time and never stored in plain text.
            </p>
          </section>

          {/* Section 5: Ephemeral Container Sandboxing */}
          <section id="ephemeral" className="space-y-4">
            <h2 className="text-xl font-bold text-content-primary tracking-tight">
              5. Ephemeral Container Lifecycle &amp; Sandboxing
            </h2>
            <p>
              When a migration dry-run or DDL test is executed (&ldquo;Run Tests&rdquo;), an ephemeral database container is provisioned exclusively for that single verification lifecycle.
            </p>
            <ul className="list-disc pl-5 space-y-2 text-xs text-content-muted">
              <li>Containers run with restricted CPU, memory (max 512MB), and read-only host filesystem overlays.</li>
              <li>Network access is bound to an internal ephemeral bridge with zero internet egress permitted.</li>
              <li>Upon test exit (pass or fail), the container is hard-killed and its ephemeral volume pruned immediately.</li>
            </ul>
          </section>

          {/* Section 6: Vulnerability Disclosure */}
          <section id="disclosure" className="space-y-4 border-t border-surface-500/80 pt-8">
            <h2 className="text-xl font-bold text-content-primary tracking-tight flex items-center gap-2">
              <Mail className="w-5 h-5 text-accent-text" />
              <span>6. Responsible Vulnerability Disclosure</span>
            </h2>
            <p>
              We welcome reports from independent security researchers and bug bounty hunters. If you believe you have discovered a potential security vulnerability in Namines, please contact us immediately:
            </p>
            <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-800/40 p-5 space-y-2 text-xs">
              <p className="text-content-primary font-semibold">
                Security Operations &amp; PGP Key:
              </p>
              <p className="font-mono text-accent-text">security@namines.com</p>
              <p className="text-content-muted leading-relaxed">
                We commit to acknowledging reports within 24 hours, providing status updates every 48 hours until remediation, and not pursuing legal action against researchers acting in good faith under standard coordinated disclosure principles.
              </p>
            </div>
          </section>
        </div>
      </div>

      <Footer />
    </div>
  );
}
