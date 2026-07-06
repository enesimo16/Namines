export interface HelpItem {
  title: string;
  description: string;
}

export const helpContent: Record<string, HelpItem> = {
  regionalPrompt: {
    title: "AI Schema Generation",
    description: "Enter a natural language description of your database requirements. The AI will translate it into a structured schema consisting of tables, columns, types, and primary/foreign key relations."
  },
  smartSeed: {
    title: "Smart Seed Data",
    description: "Generates semantic, domain-aware mock datasets mapping your schema structure. All foreign key constraints and nullable fields will be respected."
  },
  dbaAnalysis: {
    title: "AI DBA Advisor",
    description: "Runs an automated security, performance, and optimization audit on your current schema design, flagging database architectural warnings or critical errors."
  },
  branching: {
    title: "Schema Version Control",
    description: "Allows you to create isolated database schema branches. You can safely experiment, preview modifications, and merge them back with conflict resolution tools."
  }
};
