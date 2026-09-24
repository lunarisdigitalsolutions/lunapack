// @ts-check

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'LunaPack',
  tagline: 'Versioned engineering foundations for real projects.',
  favicon: 'icons/favicon.svg',
  url: 'https://lunapack.dev',
  baseUrl: '/',
  organizationName: 'lunarisdigitalsolutions',
  projectName: 'lunapack',
  onBrokenLinks: 'throw',
  markdown: {
    hooks: {
      onBrokenMarkdownLinks: 'throw'
    }
  },
  i18n: {
    defaultLocale: 'en',
    locales: ['en']
  },
  presets: [
    [
      'classic',
      /** @type {import('@docusaurus/preset-classic').Options} */
      ({
        docs: {
          path: '../../../docs/developer',
          routeBasePath: 'developer',
          sidebarPath: './sidebars.js',
          editUrl:
            'https://github.com/lunarisdigitalsolutions/lunapack/edit/main/docs/developer/'
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css'
        }
      })
    ]
  ],
  themes: [
    [
      require.resolve('@easyops-cn/docusaurus-search-local'),
      /** @type {import("@easyops-cn/docusaurus-search-local").PluginOptions} */
      ({
        hashed: true,
        indexPages: true
      })
    ]
  ],
  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      colorMode: {
        respectPrefersColorScheme: true
      },
      navbar: {
        title: 'LunaPack',
        logo: {
          alt: 'LunaPack Logo',
          src: 'icons/favicon.svg',
          width: 32,
          height: 32
        },
        items: [
          {
            type: 'docSidebar',
            sidebarId: 'developerSidebar',
            position: 'left',
            label: 'Documentation'
          },
          {
            href: 'https://github.com/lunarisdigitalsolutions/lunapack',
            label: 'GitHub',
            position: 'right'
          }
        ]
      },
      footer: {
        style: 'dark',
        logo: {
          alt: 'LunaPack Logo',
          src: 'img/logo.png',
          srcDark: 'img/logo-dark.png',
          href: 'https://lunapack.dev',
          width: 600,
          height: 160
        },
        links: [
          {
            title: 'Documentation',
            items: [
              {
                label: 'Get started',
                to: '/developer/'
              },
              {
                label: 'Pack guide',
                to: '/developer/packs/'
              }
            ]
          },
          {
            title: 'Project',
            items: [
              {
                label: 'GitHub',
                href: 'https://github.com/lunarisdigitalsolutions/lunapack'
              }
            ]
          },
          {
            title: 'Legal',
            items: [
              {
                label: 'Imprint',
                href: 'https://lunaris.digital/impressum'
              },
              {
                label: 'Data Privacy',
                href: 'https://lunaris.digital/datenschutz'
              }
            ]
          },
          {
            title: 'Support',
            items: [
              {
                label: 'Report a vulnerability',
                href: 'https://lunaris.digital/security'
              },
              {
                label: 'Report an issue',
                href: 'https://github.com/lunarisdigitalsolutions/lunapack/issues'
              },
              {
                label: 'Request a feature',
                href: 'https://github.com/lunarisdigitalsolutions/lunapack/issues'
              }
            ]
          }
        ],
        copyright: `Copyright ${new Date().getFullYear()} <a href="https://lunaris.digital">Lunaris Digital Solutions</a>.`
      }
    })
}

module.exports = config
