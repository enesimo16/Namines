import { chromium } from 'playwright';

async function testCommandPalette() {
  console.log('🚀 Running Command Palette & Shortcuts Live Verification...\n');
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  const page = await context.newPage();

  let passed = 0;
  let failed = 0;

  function assert(name, condition, message) {
    if (condition) {
      console.log(`✓ [${name}] ${message}`);
      passed++;
    } else {
      console.error(`✗ [${name}] ${message}`);
      failed++;
    }
  }

  try {
    // 1. Load Canvas page
    await page.goto('http://localhost:3000/canvas', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1500);

    // 2. Test Header Search Trigger Button
    const searchTrigger = page.locator('header button[aria-label="Open command palette"]').first();
    const isTriggerVisible = await searchTrigger.isVisible();
    assert('Header Search Pill', isTriggerVisible, 'Search pill is visible in Header');

    // Click to open Command Palette
    await searchTrigger.click();
    await page.waitForTimeout(400);

    const paletteDialog = page.locator('div[role="dialog"][aria-label="Command Palette"]');
    const isPaletteOpen = await paletteDialog.isVisible();
    assert('Command Palette Opened via Click', isPaletteOpen, 'Command palette dialog opened');

    // Test Search Input & Combobox ARIA
    const searchInput = page.locator('input[role="combobox"][aria-label*="Search"], input[placeholder*="Search commands"]');
    await searchInput.fill('Review');
    await page.waitForTimeout(300);

    const reviewOption = page.locator('#command-palette-results div[role="option"]:has-text("Review Console")').first();
    const isReviewVisible = await reviewOption.isVisible();
    assert('Command Palette Filter', isReviewVisible, 'Found Review Console in filtered results');

    // Test Keyboard Navigation (Arrow Down + Enter)
    await page.keyboard.press('ArrowDown');
    await page.waitForTimeout(100);
    await page.keyboard.press('Enter');
    await page.waitForTimeout(1500);

    const currentUrl = page.url();
    assert('Command Palette Navigation', currentUrl.includes('/review'), `Navigated to ${currentUrl}`);

    // 3. Test Global Hotkey (Ctrl+K or Meta+K) from /review
    await page.keyboard.press('Control+KeyK');
    await page.waitForTimeout(400);
    const paletteOpenViaKey = await paletteDialog.isVisible();
    assert('Command Palette Hotkey (Ctrl+K)', paletteOpenViaKey, 'Command palette opened via Ctrl+K');

    // Dismiss with Escape
    await page.keyboard.press('Escape');
    await page.waitForTimeout(300);
    const paletteClosed = !await paletteDialog.isVisible();
    assert('Command Palette Escape', paletteClosed, 'Command palette closed via Escape');

    // 4. Test Shortcuts Cheat Sheet Modal (?)
    await page.keyboard.press('?');
    await page.waitForTimeout(400);

    const shortcutsModal = page.locator('div[role="dialog"][aria-label="Keyboard Shortcuts"]');
    const isShortcutsOpen = await shortcutsModal.isVisible();
    assert('Shortcuts Modal (?)', isShortcutsOpen, 'Keyboard shortcuts modal opened via ? key');

    // Test Filter inside Shortcuts Modal
    const shortcutFilter = page.locator('input[placeholder="Filter shortcuts..."]');
    await shortcutFilter.fill('Undo');
    await page.waitForTimeout(200);

    const undoShortcut = page.locator('div:has-text("Undo last change"):visible').first();
    const isUndoVisible = await undoShortcut.isVisible();
    assert('Shortcuts Modal Filter', isUndoVisible, 'Filtered shortcuts list for "Undo"');

    // Dismiss Shortcuts Modal with Escape
    await page.keyboard.press('Escape');
    await page.waitForTimeout(300);
    const shortcutsClosed = !await shortcutsModal.isVisible();
    assert('Shortcuts Modal Escape', shortcutsClosed, 'Shortcuts modal closed via Escape');

  } catch (err) {
    console.error('Fatal error during test:', err);
    failed++;
  } finally {
    await browser.close();
  }

  console.log('\n=============================================');
  console.log(`Results: ${passed} Passed, ${failed} Failed`);
  console.log('=============================================\n');

  if (failed > 0) {
    process.exit(1);
  }
}

testCommandPalette();
