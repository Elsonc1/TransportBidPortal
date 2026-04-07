const { test, expect } = require("@playwright/test");

const SHIPPER = { email: "shipper@demo.com", password: "Demo@123" };
const CARRIER = { email: "carrier1@demo.com", password: "Demo@123" };

/** Rotulos do menu (sidebarBuild) -> data-page no DOM */
const SIDEBAR_LABEL_TO_PAGE = {
  "Unidades (CDs)": "facilities",
  "Pontos de entrega": "delivery-points",
  "Criar BID": "bid-create",
  "Template Studio": "template-studio",
  "Dashboard & Ranking": "dashboard",
  "BIDs & Propostas": "carrier-bids",
  "Perfis Mapeamento": "admin-profiles",
  "System Logs": "admin-logs"
};

async function takeStepShot(page, testInfo, name) {
  await page.screenshot({
    path: testInfo.outputPath(`${name}.png`),
    fullPage: true
  });
}

async function login(page, creds) {
  await page.goto("/");
  await page.fill("#email", creds.email);
  await page.fill("#password", creds.password);
  await page.click("button:has-text('Login')");
  await expect(page.locator("#appShell")).toBeVisible();
}

async function navigateSidebar(page, label) {
  const isMobile = await page.evaluate(() => window.matchMedia("(max-width: 1024px)").matches);
  if (isMobile) {
    const drawerOpen = await page.locator("#sidebar").evaluate(el => el.classList.contains("sidebar--mobile-open"));
    if (!drawerOpen) {
      await page.locator("#sidebarToggleBtn").click();
      await expect(page.locator("#sidebarBackdrop")).toBeVisible();
      await expect(page.locator("#sidebar")).toHaveClass(/sidebar--mobile-open/);
    }
    await expect(page.locator("#sidebarNav")).toBeVisible();
  }

  const pageId = SIDEBAR_LABEL_TO_PAGE[label];
  const item = pageId
    ? page.locator(`.sb-item[data-page="${pageId}"]`).first()
    : page.locator(".sb-item", { hasText: label }).first();

  if (isMobile) {
    // sidebar__nav mantem scroll entre aberturas; forca alvo dentro da area rolavel
    await item.evaluate((el) => el.scrollIntoView({ block: "nearest", inline: "nearest" }));
  }
  await item.scrollIntoViewIfNeeded();
  await expect(item).toBeInViewport();
  await item.click();
}

test("mobile 390x844 smoke - shipper flows + dashboard", async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== "mobile-chrome-390x844");

  // 1) Login + drawer/backdrop/ESC
  await login(page, SHIPPER);
  await page.click("#sidebarToggleBtn");
  await expect(page.locator("#sidebarBackdrop")).toBeVisible();
  await takeStepShot(page, testInfo, "01-mobile-login-drawer-open");

  // Clique fora do drawer (area de backdrop) para fechar.
  await page.mouse.click(380, 120);
  await expect(page.locator("#sidebarBackdrop")).toBeHidden();
  await page.click("#sidebarToggleBtn");
  await page.keyboard.press("Escape");
  await expect(page.locator("#sidebarBackdrop")).toBeHidden();
  await takeStepShot(page, testInfo, "01-mobile-drawer-closed");

  // 2) Navegação shipper: Facilities -> Delivery Points -> Criar BID -> Dashboard
  await navigateSidebar(page, "Unidades (CDs)");
  await expect(page.locator('section[data-page="facilities"]:not(.hidden)')).toBeVisible();
  await navigateSidebar(page, "Pontos de entrega");
  await expect(page.locator('section[data-page="delivery-points"]:not(.hidden)')).toBeVisible();
  await navigateSidebar(page, "Criar BID");
  await expect(page.locator('section[data-page="bid-create"]:not(.hidden)')).toBeVisible();
  await navigateSidebar(page, "Dashboard & Ranking");
  await expect(page.locator('section[data-page="dashboard"]:not(.hidden)')).toBeVisible();
  await takeStepShot(page, testInfo, "02-mobile-shipper-navigation");

  // 3) Form Facilities sem quebra
  await navigateSidebar(page, "Unidades (CDs)");
  await page.click("button:has-text('Nova Unidade')");
  await expect(page.locator("#facilityForm")).toBeVisible();
  await expect(page.locator("#facilityCnpj")).toBeVisible();
  await expect(page.locator("button:has-text('Salvar')").first()).toBeVisible();
  await takeStepShot(page, testInfo, "03-mobile-facilities-form");
  await page.click("#facilityForm button:has-text('Cancelar')");

  // 4) Form Delivery Points sem quebra (inclui ação de CEP)
  await navigateSidebar(page, "Pontos de entrega");
  await page.click("button:has-text('Novo ponto')");
  await expect(page.locator("#dpForm")).toBeVisible();
  await page.fill("#dpZip", "01310100");
  await page.click("#dpForm button:has-text('Buscar CEP')");
  await expect(page.locator("#dpCepHint")).toBeVisible();
  await takeStepShot(page, testInfo, "04-mobile-delivery-points-form");
  await page.click("#dpForm button:has-text('Cancelar')");

  // 5) Criar BID: grid de lanes usável
  await navigateSidebar(page, "Criar BID");
  await expect(page.locator("#bidLanesGrid")).toBeVisible();
  const originSelect = page.locator('#bidLanesBody tr:first-child select[data-key="Origin"]');
  const destSelect = page.locator('#bidLanesBody tr:first-child select[data-key="Destination"]');
  await originSelect.selectOption({ index: 1 });
  await destSelect.selectOption({ index: 1 });
  await page.fill('#bidLanesBody tr:first-child input[data-key="Region"]', "SE");
  await expect(page.locator("button:has-text('Criar BID')")).toBeVisible();
  await takeStepShot(page, testInfo, "05-mobile-bid-lanes");

  // 7) Dashboard sem corte grave
  await navigateSidebar(page, "Dashboard & Ranking");
  await expect(page.locator("#kpiSavings")).toBeVisible();
  await expect(page.locator("#priceChart")).toBeVisible();
  await takeStepShot(page, testInfo, "07-mobile-dashboard");
});

test("mobile 390x844 smoke - carrier proposal form", async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== "mobile-chrome-390x844");

  // 6) Carrier: proposta usável
  await login(page, CARRIER);
  await expect(page.locator('section[data-page="carrier-bids"]:not(.hidden)')).toBeVisible();
  await expect(page.locator("#carrierBidSelect")).toBeVisible();
  await expect(page.locator("#capacity")).toBeVisible();
  await expect(page.locator("button:has-text('Save Draft')")).toBeVisible();
  await expect(page.locator("button:has-text('Submit Final')")).toBeVisible();
  await takeStepShot(page, testInfo, "06-mobile-carrier-proposal");
});

test("desktop >=1280 smoke - layout and regressions", async ({ page }, testInfo) => {
  test.skip(testInfo.project.name !== "desktop-chromium");

  await login(page, SHIPPER);
  await page.setViewportSize({ width: 1366, height: 900 });
  await expect(page.locator("#sidebar")).toBeVisible();
  await expect(page.locator("#appContent")).toBeVisible();

  // 8) Sidebar/layout sem regressão
  await expect(page.locator(".topbar")).toBeVisible();
  await expect(page.locator(".sb-item", { hasText: "Unidades (CDs)" })).toBeVisible();
  await takeStepShot(page, testInfo, "08-desktop-sidebar-layout");

  // 9) Grids/tabelas sem regressão visual crítica
  await navigateSidebar(page, "Unidades (CDs)");
  await expect(page.locator("#facilityGrid thead")).toBeVisible();
  await navigateSidebar(page, "Criar BID");
  await expect(page.locator("#bidLanesHead")).toBeVisible();
  await navigateSidebar(page, "Dashboard & Ranking");
  await expect(page.locator("#priceChart")).toBeVisible();
  await takeStepShot(page, testInfo, "09-desktop-grids-dashboard");

  // 10) Formularios principais usáveis no desktop
  await navigateSidebar(page, "Pontos de entrega");
  await page.click("button:has-text('Novo ponto')");
  await expect(page.locator("#dpName")).toBeVisible();
  await expect(page.locator("#dpForm button:has-text('Salvar')")).toBeVisible();
  await navigateSidebar(page, "Criar BID");
  await expect(page.locator("#bidTitle")).toBeVisible();
  await expect(page.locator("button:has-text('Criar BID')")).toBeVisible();
  await takeStepShot(page, testInfo, "10-desktop-forms");
});
