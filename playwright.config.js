const { defineConfig, devices } = require("@playwright/test");

module.exports = defineConfig({
  testDir: "./tests/ui",
  timeout: 90_000,
  expect: { timeout: 10_000 },
  fullyParallel: false,
  workers: 1,
  retries: 0,
  reporter: [
    ["list"],
    ["html", { outputFolder: "artifacts/ui/playwright-report", open: "never" }],
    ["json", { outputFile: "artifacts/ui/smoke-summary.json" }]
  ],
  outputDir: "artifacts/ui/test-results",
  use: {
    baseURL: "http://127.0.0.1:5157",
    trace: "retain-on-failure",
    screenshot: "only-on-failure",
    video: "off"
  },
  projects: [
    {
      name: "desktop-chromium",
      use: {
        ...devices["Desktop Chrome"],
        viewport: { width: 1366, height: 900 }
      }
    },
    {
      name: "mobile-chrome-390x844",
      use: {
        ...devices["Pixel 5"],
        viewport: { width: 390, height: 844 }
      }
    }
  ],
  webServer: {
    command: "dotnet run --project TransportBidPortal.csproj --urls http://127.0.0.1:5157",
    url: "http://127.0.0.1:5157",
    timeout: 120_000,
    reuseExistingServer: true
  }
});
