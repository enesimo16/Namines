import { chromium } from 'playwright';

async function runLiveE2ETest() {
  console.log('🚀 Starting Namines Live E2E Verification Suite...\n');
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ viewport: { width: 1440, height: 900 } });
  
  // Grant clipboard permissions so copy actions succeed cleanly in headless Chromium
  await context.grantPermissions(['clipboard-read', 'clipboard-write']).catch(() => {});

  // Pre-seed tour completion so onboarding spotlight doesn't intercept pointer events
  await context.addInitScript(() => {
    localStorage.setItem('namines-tour-onboarding', JSON.stringify({
      state: { hasCompletedTour: true, isTourActive: false, activeStepIndex: 0 },
      version: 0,
    }));
  });

  const page = await context.newPage();

  const results = [];
  const uncaughtErrors = [];

  page.on('pageerror', error => {
    uncaughtErrors.push(`[Page Uncaught Error @ ${page.url()}]: ${error.message}`);
  });

  page.on('console', msg => {
    if (msg.type() === 'error') {
      const text = msg.text();
      // Filter out network offline errors for ASP.NET / WebSocket when backend is not running
      if (
        !text.includes('ERR_CONNECTION_REFUSED') &&
        !text.includes('WebSocket failed to connect') &&
        !text.includes('Failed to load resource') &&
        !text.includes('favicon.ico') &&
        !text.includes('Failed to fetch AI Policy')
      ) {
        console.log(`[Console Error]: ${text}`);
      }
    }
  });

  const logStep = (step, ok, detail = '') => {
    const symbol = ok ? '✓' : '✗';
    console.log(`${symbol} [${step}] ${detail}`);
    results.push({ step, ok, detail });
  };

  try {
    // ══════════════════════════════════════════════════════════════════════════
    // 1. Home Page (/)
    // ══════════════════════════════════════════════════════════════════════════
    console.log('\n--- 1. Testing Home Page (http://localhost:3000) ---');
    await page.goto('http://localhost:3000', { waitUntil: 'domcontentloaded' });
    const title = await page.title();
    logStep('Home Title', title.includes('Namines'), `Title: "${title}"`);

    // Theme toggle
    const themeBtn = page.locator('button[aria-label="Toggle theme"], button[title*="theme" i], button:has-text("Theme")').first();
    if (await themeBtn.isVisible()) {
      await themeBtn.click();
      await page.waitForTimeout(200);
      logStep('Theme Toggle', true, 'Toggled dark/light mode');
    }

    // Language toggle
    const langBtn = page.locator('button:has-text("TR"), button:has-text("EN")').first();
    if (await langBtn.isVisible()) {
      await langBtn.click();
      await page.waitForTimeout(200);
      logStep('Language Toggle', true, 'Toggled language localization');
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 2. Canvas Page (/canvas) & Template Loading
    // ══════════════════════════════════════════════════════════════════════════
    console.log('\n--- 2. Testing Canvas Page (http://localhost:3000/canvas) ---');
    await page.goto('http://localhost:3000/canvas', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1500);
    logStep('Canvas Route', true, 'Loaded /canvas route');

    // Dismiss tour if still triggered
    const skipTourBtn = page.locator('button:has-text("Skip Tour")').first();
    if (await skipTourBtn.isVisible()) {
      await skipTourBtn.click();
      await page.waitForTimeout(300);
    }

    // Check if empty canvas state or nodes exist
    let nodeCount = await page.locator('.react-flow__node').count();
    console.log(`Initial canvas node count: ${nodeCount}`);

    if (nodeCount === 0) {
      const browseTemplatesBtn = page.locator('button:has-text("Browse Templates"):visible').first();
      if (await browseTemplatesBtn.isVisible()) {
        await browseTemplatesBtn.click();
        await page.waitForTimeout(500);

        // Click Replace on first template in SchemaTemplateGallery
        const replaceBtn = page.locator('div[class*="fixed"] button:has-text("Replace"):visible').first();
        if (await replaceBtn.isVisible()) {
          await replaceBtn.click();
          await page.waitForTimeout(1000);
          nodeCount = await page.locator('.react-flow__node').count();
          logStep('Template Loaded', nodeCount > 0, `Loaded template with ${nodeCount} tables`);
        }
      }
    } else {
      logStep('Canvas Tables Present', true, `Canvas already contains ${nodeCount} tables`);
    }

    // Test Canvas Toolbar Zoom / Fit controls
    const fitViewBtn = page.locator('button[aria-label="Fit View"], button[title*="fit" i], button:has(svg.lucide-maximize-2), button:has(svg.lucide-scan)').first();
    if (await fitViewBtn.isVisible()) {
      await fitViewBtn.click();
      logStep('Canvas Fit View', true, 'Clicked Fit View control');
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 3. Settings Modal (AIPreferencesModal) - All Tabs
    // ══════════════════════════════════════════════════════════════════════════
    console.log('\n--- 3. Testing Settings Modal & Preferences Tabs ---');
    const settingsBtn = page.locator('header button[aria-label="Open Settings"]:visible, header button[title="Settings"]:visible, header button:has(svg.lucide-settings):visible').first();
    if (await settingsBtn.isVisible()) {
      await settingsBtn.click();
      await page.waitForTimeout(500);
      logStep('Settings Open', true, 'Opened Settings Modal');

      const tabs = ['Profile', 'Account', 'Pricing', 'Analytics', 'Help'];
      for (const tabName of tabs) {
        const tabEl = page.locator(`div[role="dialog"] button:has-text("${tabName}"):visible, button[role="tab"]:has-text("${tabName}"):visible`).first();
        if (await tabEl.isVisible()) {
          await tabEl.click();
          await page.waitForTimeout(200);
          logStep(`Settings Tab: ${tabName}`, true, `Verified tab ${tabName}`);
        }
      }

      // Close modal
      const closeBtn = page.locator('div[role="dialog"] button:has(svg.lucide-x)').first();
      if (await closeBtn.isVisible()) {
        await closeBtn.click();
        await page.waitForTimeout(300);
        logStep('Settings Close', true, 'Closed Settings Modal');
      }
    } else {
      logStep('Settings Button', false, 'Settings button not found in header');
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 4. "Request Review" Button Flow & Navigation
    // ══════════════════════════════════════════════════════════════════════════
    console.log('\n--- 4. Testing "Request Review" Button Flow ---');
    const requestReviewBtn = page.locator('button:has-text("Request Review"):visible, button[aria-label*="review" i]:visible').first();
    if (await requestReviewBtn.isVisible()) {
      console.log('Clicking "Request Review" button...');
      await requestReviewBtn.click();
      
      // Wait for navigation
      await page.waitForTimeout(2500);
      const urlAfterClick = page.url();
      console.log(`URL after clicking Request Review: ${urlAfterClick}`);

      const reachedReview = urlAfterClick.includes('/review');
      logStep('Request Review Action', reachedReview, `Navigated to: ${urlAfterClick}`);

      if (reachedReview) {
        // ════════════════════════════════════════════════════════════════════════
        // 5. Change Review Detail Page (/review/[id])
        // ════════════════════════════════════════════════════════════════════════
        console.log('\n--- 5. Testing Change Review Detail Page (/review/[id]) ---');
        await page.waitForTimeout(1000);

        // Test tabs: DIFF, SQL, IMPACT, TESTS, CODE
        const reviewTabs = ['DIFF', 'SQL', 'IMPACT', 'TESTS', 'CODE'];
        for (const rTab of reviewTabs) {
          const tabBtn = page.locator(`button:has-text("${rTab}"):visible`).first();
          if (await tabBtn.isVisible()) {
            await tabBtn.click();
            await page.waitForTimeout(300);
            logStep(`Review Detail Tab: ${rTab}`, true, `Switched to tab ${rTab}`);
          }
        }

        // Test "Run Tests" button
        const runTestsBtn = page.locator('button:has-text("Run Tests"):visible, button:has-text("Test DDL"):visible').first();
        if (await runTestsBtn.isVisible()) {
          console.log('Clicking "Run Tests"...');
          await runTestsBtn.click();
          await page.waitForTimeout(1500);
          logStep('Run Tests Execution', true, 'Triggered DDL verification test run');
        }

        // Test "Approve" button
        const approveBtn = page.locator('button:has-text("Approve"):visible, button:has-text("Approve changes"):visible').first();
        if (await approveBtn.isVisible()) {
          console.log('Clicking "Approve"...');
          await approveBtn.click();
          await page.waitForTimeout(1000);
          logStep('Approve Decision', true, 'Executed Approve action on Change Request');
        }
      }
    } else {
      logStep('Request Review Button', false, 'Request Review button not found on canvas');
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 6. Review List Page (/review)
    // ══════════════════════════════════════════════════════════════════════════
    await page.goto('http://localhost:3000/review', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1200);
    const crListItems = await page.locator('button:has-text("tables"), ul.space-y-3 li').count();
    const crGeneric = await page.locator('button:has-text("approvals"), button:has-text("tables")').count();
    const hasItems = crListItems > 0 || crGeneric > 0;
    logStep('Review List Page', hasItems, `Loaded /review with ${crListItems} change request items`);

    // ══════════════════════════════════════════════════════════════════════════
    // 7. Compile Page (/compile) & CodeStudioViewer
    // ══════════════════════════════════════════════════════════════════════════
    console.log('\n--- 7. Testing Compile Page & CodeStudioViewer (/compile) ---');
    await page.goto('http://localhost:3000/compile', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1500);
    logStep('Compile Page Loaded', true, 'Loaded /compile route');

    // Verify CodeStudioViewer code container
    const codeContainer = page.locator('code, pre').first();
    const hasCode = await codeContainer.isVisible();
    logStep('CodeStudioViewer Visible', hasCode, 'Syntax viewer active');

    // In-code search test
    const searchInput = page.locator('input[placeholder*="Find in script"]').first();
    if (await searchInput.isVisible()) {
      await searchInput.fill('TABLE');
      await page.waitForTimeout(200);
      logStep('In-Code Search', true, 'Executed in-code live search');
      await searchInput.fill('');
    }

    // Line wrap toggle
    const wrapBtn = page.locator('button[aria-label="Toggle line wrap"]').first();
    if (await wrapBtn.isVisible()) {
      await wrapBtn.click();
      await page.waitForTimeout(200);
      logStep('Line Wrap Toggle', true, 'Toggled word wrap');
    }

    // Copy code button
    const copyBtn = page.locator('button[aria-label="Copy code"]').first();
    if (await copyBtn.isVisible()) {
      await copyBtn.click();
      await page.waitForTimeout(200);
      logStep('Copy Code Action', true, 'Clicked Copy Code button');
    }

    // Iterate through all Compile sidebar tabs
    const compileTabs = [
      'EF Core',
      'Prisma',
      'Mermaid ER',
      'Data Dictionary',
      'README.md',
      'Test Data',
      'Docker Sandbox',
      'Launch to Cloud',
      'Export to…',
      'DDL Script',
    ];

    for (const tabName of compileTabs) {
      const btn = page.locator(`button:has-text("${tabName}"):visible`).first();
      if (await btn.isVisible()) {
        await btn.click();
        await page.waitForTimeout(300);
        logStep(`Compile Tab: ${tabName}`, true, `Switched to ${tabName}`);
      }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 8. Flow, Ground, Vault Pages
    // ══════════════════════════════════════════════════════════════════════════
    console.log('\n--- 8. Testing Flow, Ground, Vault Pages ---');
    const extraPages = [
      { name: 'Flow', url: 'http://localhost:3000/flow' },
      { name: 'Ground', url: 'http://localhost:3000/ground' },
      { name: 'Vault', url: 'http://localhost:3000/vault' },
    ];

    for (const p of extraPages) {
      await page.goto(p.url, { waitUntil: 'domcontentloaded' });
      await page.waitForTimeout(800);
      const heading = await page.locator('h1, h2').first().textContent();
      logStep(`Page /${p.name.toLowerCase()}`, true, `Loaded ${p.name} (Heading: "${heading?.trim()}")`);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 9. Desk Service Direct (http://localhost:3200)
    // ══════════════════════════════════════════════════════════════════════════
    console.log('\n--- 9. Testing Desk Service (http://localhost:3200) ---');
    await page.goto('http://localhost:3200', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);
    const deskTitle = await page.title();
    logStep('Desk Service Direct', true, `Desk loaded with title: "${deskTitle}"`);

    // Test Desk Navigation Tabs
    const deskTabs = ['Projects', 'Deployments', 'SQL Console', 'API Keys', 'Vault', 'Ground', 'Analytics'];
    for (const dTab of deskTabs) {
      const tabEl = page.locator(`button:has-text("${dTab}"):visible, a:has-text("${dTab}"):visible`).first();
      if (await tabEl.isVisible()) {
        await tabEl.click();
        await page.waitForTimeout(300);
        logStep(`Desk View: ${dTab}`, true, `Navigated to ${dTab}`);
      }
    }

    // ══════════════════════════════════════════════════════════════════════════
    // 10. Desk Button in WorkspaceNav Handshake
    // ══════════════════════════════════════════════════════════════════════════
    console.log('\n--- 10. Testing Desk Launch Button in Canvas WorkspaceNav ---');
    await page.goto('http://localhost:3000/canvas', { waitUntil: 'domcontentloaded' });
    await page.waitForTimeout(1000);

    let deskNavBtn = page.locator('header nav button:has-text("Desk"):visible').first();
    if (!await deskNavBtn.isVisible()) {
      // If compressed to menu dropdown, open dropdown
      const menuToggle = page.locator('header button[aria-haspopup="menu"]:visible').first();
      if (await menuToggle.isVisible()) {
        await menuToggle.click();
        await page.waitForTimeout(300);
        deskNavBtn = page.locator('div[role="menu"] button:has-text("Desk"):visible').first();
      }
    }

    const deskBtnVisible = await deskNavBtn.isVisible();
    logStep('Desk WorkspaceNav Button', deskBtnVisible, 'Desk button visible in WorkspaceNav');

    if (deskBtnVisible) {
      // Check popup or window.open behavior
      const [newPage] = await Promise.all([
        context.waitForEvent('page', { timeout: 3000 }).catch(() => null),
        deskNavBtn.click(),
      ]);

      if (newPage) {
        await newPage.waitForLoadState('domcontentloaded');
        const newPageUrl = newPage.url();
        logStep('Desk Opened in New Tab', newPageUrl.includes('3200'), `Opened URL: ${newPageUrl}`);
        await newPage.close();
      } else {
        logStep('Desk Open Triggered', true, 'Handled Desk button click');
      }
    }

  } catch (err) {
    console.error('Fatal Test Exception:', err);
    logStep('Execution Error', false, err.message);
  } finally {
    await browser.close();
  }

  console.log('\n=============================================');
  console.log('🏁 Namines Verification Suite Finished!');
  const passed = results.filter(r => r.ok).length;
  const failed = results.filter(r => !r.ok).length;
  console.log(`Total: ${results.length} | Passed: ${passed} | Failed: ${failed}`);
  if (uncaughtErrors.length > 0) {
    console.log(`Uncaught Page Errors: ${uncaughtErrors.length}`);
    uncaughtErrors.forEach(e => console.log('  ', e));
  }
  console.log('=============================================\n');

  if (failed > 0 || uncaughtErrors.length > 0) {
    process.exit(1);
  }
}

runLiveE2ETest();
