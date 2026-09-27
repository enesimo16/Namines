/**
 * İngilizce — kaynak dil. Anahtarların tek kaynağı bu dosya; öteki diller
 * `Messages` tipiyle birebir bu şekle uymak zorunda.
 *
 * Kapsam (2026-09-27): uygulamanın ortak kabuğu — üst menü, çalışma alanı
 * gezinmesi, ayarlar modalının gezinmesi ve başlıkları. Ekranların içeriği
 * henüz buraya taşınmadı; bkz. docs/audit/22-product-backlog.md B-56.
 */
const en = {
  language: {
    label: 'Language',
    en: 'English',
    tr: 'Türkçe (beta)',
    betaNote: 'Only the app shell is translated so far; screens are still in English.',
  },
  header: {
    homeNav: {
      howItWorks: 'How it works',
      engines: 'Engines',
      zeroDrift: 'Zero Drift',
      architecture: 'Architecture',
      ecosystem: 'Ecosystem',
      security: 'Security',
    },
    demo: 'Demo',
    exploreDemo: 'Explore Demo',
    signIn: 'Sign In',
    getStarted: 'Get Started',
    backToTop: 'Back to top',
    goToHomepage: 'Go to homepage',
    scrollBackToTop: 'Scroll back to top',
    goToNaminesHomepage: 'Go to Namines homepage',
    workspace: 'Workspace',
    workspaceTitle: 'Workspace — your saved projects',
    team: 'Team',
    teamTitle: 'Team — members and shared projects',
    projectName: 'Project name',
    save: 'Save',
    editProjectName: 'Edit project name',
    settings: 'Settings',
    openSettings: 'Open Settings',
    openAccountSettings: 'Open account and AI settings',
    logOut: 'Log Out',
    loggedOut: 'Logged out successfully.',
    loginSignUp: 'Login / Sign Up',
    login: 'Login',
    plan: {
      dev: 'DEV',
      team: 'TEAM MEMBER',
      pro: 'PRO MEMBER',
      free: 'FREE MEMBER',
    },
    quota: {
      exhausted: 'Out of tokens — minimum AI is active. All free features stay available.',
      low: 'AI tokens: {percent}% left — switching to minimum AI soon.',
      remaining: 'AI tokens: {percent}% left.',
      dismiss: 'Dismiss alert',
    },
  },
  workspaceNav: {
    label: 'Workspace',
    diagram: 'Diagram',
    flow: 'Flow',
    ground: 'Ground',
    vault: 'Vault',
    desk: 'Desk',
    deskNeedsProject: 'Open or save a project first — Desk needs one to open.',
    deskFailed: 'Could not open Desk. Try again.',
  },
  preferences: {
    hub: 'Namines Hub',
    hubSubtitle: 'Settings & Panel',
    logOut: 'Log Out',
    close: 'Close',
    tabs: {
      profile: { nav: 'Profile Settings', title: 'User Profile Settings', subtitle: 'Configure developer identity and portfolio information.' },
      account: { nav: 'Account & Tokens', title: 'Account Credentials & Tokens', subtitle: 'View access details, quotas, and manage API tokens.' },
      ai: { nav: 'AI Configurations', title: 'AI Services Routing', subtitle: 'Configure custom LLM routing and advanced parameters.' },
      analytics: { nav: 'System Analytics', title: 'System Usage & Analytics', subtitle: 'Visual summary of database compiles, seeding logs, and AI request cycles.' },
      pricing: { nav: 'Pricing', title: 'Membership Plans', subtitle: 'Explore plan differences and upgrade.' },
      help: { nav: 'Help & FAQ', title: 'Help & FAQ', subtitle: 'Frequently Asked Questions & support guidelines.' },
    },
  },
} as const;

export default en;
