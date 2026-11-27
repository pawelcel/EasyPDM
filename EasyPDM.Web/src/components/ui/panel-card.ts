// A card of a detail panel: the item and project panels are stacks of these on the page
// background, the same cards and gaps as the sections of Settings (e.g. Settings -> Users), so
// long lists (BOM, attachments, history, order documents) stand apart from the fields. A card's
// first section heading drops its top margin, which only spaces headings within a card.
export const PANEL_CARD =
  "rounded-xl bg-card p-4 ring-1 ring-foreground/10 [&>h4:first-child]:mt-0 [&>div:first-child>h4]:mt-0"
