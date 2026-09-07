'use client';

import Link from 'next/link';
import { ArrowRight, CheckCircle2, GitPullRequest } from 'lucide-react';

export default function ZeroDriftSection() {
  return (
    <section id="zero-drift" className="w-full max-w-[var(--w-app)] mx-auto px-4 sm:px-6 lg:px-8 py-20 sm:py-28">
      {/* Section Title with Gradient Highlight */}
      <div className="mb-12 sm:mb-16">
        <h2 className="text-3xl sm:text-4xl lg:text-5xl font-bold tracking-tight text-content-primary">
          Design schemas and migrations with{' '}
          <span
            className="bg-clip-text text-transparent inline-block font-extrabold"
            style={{
              backgroundImage: 'var(--namines-gradient-headline)',
            }}
          >
            zero drift
          </span>
        </h2>
      </div>

      {/* 2-Column Cards Grid */}
      <div className="grid grid-cols-1 lg:grid-cols-2 gap-6 lg:gap-8 items-stretch">
        {/* Card 1: Typographic Highlight List */}
        <div className="rounded-[var(--radius-modal)] border border-surface-500 bg-surface-900/90 p-8 sm:p-10 flex flex-col justify-center shadow-xl">
          <p className="text-xl sm:text-2xl font-medium leading-relaxed text-content-primary">
            Intuitive database architecture and lifecycle safety for{' '}
            <span style={{ color: 'oklch(80% 0.14 var(--brand-hue))' }}>PostgreSQL schemas</span>,{' '}
            <span style={{ color: 'oklch(78% 0.14 160)' }}>foreign key cascades</span>,{' '}
            <span style={{ color: 'oklch(82% 0.12 180)' }}>indexes &amp; constraints</span>,{' '}
            <span style={{ color: 'oklch(78% 0.13 210)' }}>multi-engine DDL</span>,{' '}
            <span style={{ color: 'oklch(80% 0.14 var(--brand-hue))' }}>EF Core &amp; Prisma</span>,{' '}
            <span style={{ color: 'oklch(78% 0.14 150)' }}>visual ERD canvas</span>,{' '}
            <span style={{ color: 'oklch(82% 0.12 195)' }}>drift detection</span>,{' '}
            <span style={{ color: 'oklch(78% 0.14 165)' }}>ephemeral sandboxes</span>,{' '}
            <span style={{ color: 'oklch(78% 0.13 210)' }}>live CRUD panels</span>, and{' '}
            <span style={{ color: 'oklch(80% 0.14 var(--brand-hue))' }}>schema versioning</span>.
          </p>
        </div>

        {/* Card 2: Full-stack Previews for Every Pull Request */}
        <div className="rounded-[var(--radius-modal)] border border-surface-500 bg-surface-900/90 p-8 sm:p-10 flex flex-col justify-between shadow-xl">
          <div>
            <h3 className="text-xl sm:text-2xl font-bold text-content-primary mb-3">
              Full-stack schema diffs for every pull request
            </h3>
            <p className="text-xs sm:text-sm text-content-muted leading-relaxed mb-5">
              Iterate quickly with ephemeral validations of your entire database architecture for every change before merging.
            </p>
            <Link
              href="/demo"
              className="inline-flex items-center gap-1.5 text-xs font-mono text-accent-text hover:text-content-primary transition-colors mb-8"
            >
              <span>Explore schema diff docs</span>
              <ArrowRight className="w-3.5 h-3.5" />
            </Link>
          </div>

          {/* PR Mockup Card */}
          <div className="rounded-[var(--radius-card)] border border-surface-500 bg-surface-800/80 p-4 flex flex-col sm:flex-row items-center gap-4">
            {/* PR Info */}
            <div className="flex flex-col gap-1 w-full sm:w-1/2 pr-0 sm:pr-4 border-b sm:border-b-0 sm:border-r border-surface-500/70 pb-3 sm:pb-0">
              <div className="flex items-center justify-between text-xs font-mono">
                <span className="font-bold text-content-primary flex items-center gap-1.5">
                  <GitPullRequest className="w-3.5 h-3.5 text-accent-text" />
                  PR / 142
                </span>
                <span className="text-[11px] text-content-subtle">Open preview &gt;</span>
              </div>
              <span className="text-xs font-mono text-content-secondary">
                feature/database-branch
              </span>
              <span className="text-[11px] font-mono text-success flex items-center gap-1 mt-1">
                <CheckCircle2 className="w-3.5 h-3.5 text-success" />
                Checks passed
              </span>
            </div>

            {/* Preview Services Status */}
            <div className="flex flex-col gap-1.5 w-full sm:w-1/2">
              <span className="text-[10px] font-mono uppercase tracking-wider text-content-subtle">
                preview-env
              </span>
              <div className="flex items-center justify-between text-xs font-mono">
                <span className="flex items-center gap-1.5 text-content-secondary">
                  <span className="w-1.5 h-1.5 rounded-full bg-success" />
                  web
                </span>
                <span className="text-content-subtle">Available</span>
              </div>
              <div className="flex items-center justify-between text-xs font-mono">
                <span className="flex items-center gap-1.5 text-content-secondary">
                  <span className="w-1.5 h-1.5 rounded-full bg-success" />
                  api
                </span>
                <span className="text-content-subtle">Available</span>
              </div>
              <div className="flex items-center justify-between text-xs font-mono">
                <span className="flex items-center gap-1.5 text-content-secondary">
                  <span className="w-1.5 h-1.5 rounded-full bg-success" />
                  database
                </span>
                <span className="text-content-subtle">Available</span>
              </div>
              <div className="flex items-center justify-between text-xs font-mono">
                <span className="flex items-center gap-1.5 text-content-secondary">
                  <span className="w-1.5 h-1.5 rounded-full bg-success" />
                  desk-admin
                </span>
                <span className="text-content-subtle">Available</span>
              </div>
            </div>
          </div>
        </div>
      </div>
    </section>
  );
}
