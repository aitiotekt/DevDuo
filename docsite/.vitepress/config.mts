import { join } from "node:path";
import { defineConfig } from "vitepress";
import { manifest, siteHref } from "../../scripts/lib/docs.mts";
import { loadMetadata } from "../../scripts/lib/metadata.mts";
import { repoRoot } from "../../scripts/lib/paths.mts";

const metadata = loadMetadata();

function sidebar(prefix: string, language: "en" | "zh") {
  const sections = [
    { id: "development", en: "Development", zh: "开发入口" },
    { id: "calibration", en: "Calibration reference", zh: "标定参考" },
    { id: "topics", en: "Read as needed", zh: "按需专题" },
  ];
  return sections.map((section) => ({
    text: section[language],
    items: [
      ...(section.id === "development"
        ? [
            {
              text: language === "en" ? "Development guide" : "开发与贡献指南",
              link: language === "en" ? "/contributing" : "/zh/contributing",
            },
          ]
        : []),
      ...manifest.documents
        .filter((doc) => doc.section === section.id)
        .map((doc) => ({
          text: doc[language],
          link: `${prefix}/${doc.name.replace(/\.md$/, "")}`,
        })),
    ],
  }));
}

export default defineConfig({
  title: metadata.project.displayName,
  description: "Pesudo iPhone Duo Viewport Simulator for Web Developers",
  base: new URL(metadata.project.docsUrl).pathname,
  cleanUrls: true,
  outDir: join(repoRoot, "temp/build/docsite"),
  cacheDir: join(repoRoot, "temp/cache/vitepress"),
  srcExclude: manifest.conventions.flatMap((name) => [
    `docs/${name}`,
    `zh/docs/${name}`,
  ]),
  lastUpdated: false,
  vite: { resolve: { preserveSymlinks: true } },
  markdown: {
    config(markdown) {
      const fallback = markdown.renderer.rules.link_open;
      markdown.renderer.rules.link_open = (
        tokens,
        index,
        options,
        environment,
        self,
      ) => {
        const token = tokens[index],
          href = token.attrGet("href");
        if (href)
          token.attrSet(
            "href",
            siteHref(
              href,
              String(environment.path ?? environment.realPath ?? ""),
            ),
          );
        return fallback
          ? fallback(tokens, index, options, environment, self)
          : self.renderToken(tokens, index, options);
      };
    },
  },
  themeConfig: {
    search: { provider: "local" },
    socialLinks: [{ icon: "github", link: metadata.project.repositoryUrl }],
    footer: {
      message:
        "Independent project · first-party code: MIT · simulator artwork: see third-party notices",
      copyright: "Copyright © 2026 DevDuo contributors",
    },
  },
  locales: {
    root: {
      label: "English",
      lang: "en",
      themeConfig: {
        nav: [
          { text: "Home", link: "/" },
          { text: "Docs", link: "/docs/001-ARCHITECTURE" },
          { text: "Security", link: "/security" },
          { text: "Privacy", link: "/privacy" },
          { text: "Contributing", link: "/contributing" },
          { text: "Changelog", link: "/changelog" },
        ],
        sidebar: {
          "/docs/": sidebar("/docs", "en"),
          "/contributing": sidebar("/docs", "en"),
        },
      },
    },
    zh: {
      label: "中文",
      lang: "zh",
      link: "/zh/",
      themeConfig: {
        nav: [
          { text: "首页", link: "/zh/" },
          { text: "文档", link: "/zh/docs/001-ARCHITECTURE" },
          { text: "安全", link: "/zh/security" },
          { text: "隐私", link: "/zh/privacy" },
          { text: "贡献", link: "/zh/contributing" },
          { text: "变更日志", link: "/zh/changelog" },
        ],
        sidebar: {
          "/zh/docs/": sidebar("/zh/docs", "zh"),
          "/zh/contributing": sidebar("/zh/docs", "zh"),
        },
      },
    },
  },
});
