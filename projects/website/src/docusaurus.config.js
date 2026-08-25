// @ts-check

/** @type {import('@docusaurus/types').Config} */
const config = {
  title: 'LunaPack',
  tagline: 'Versioned engineering foundations for real projects.',
  favicon: 'img/favicon.svg',
  url: 'https://lunarisdigitalsolutions.github.io',
  baseUrl: '/lunapack/',
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
  themeConfig:
    /** @type {import('@docusaurus/preset-classic').ThemeConfig} */
    ({
      image: 'img/lunapack-pack-workflow.png',
      navbar: {
        title: 'LunaPack',
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
          }
        ],
        copyright: `Copyright ${new Date().getFullYear()} <a href="https://lunaris.digital">Lunaris Digital Solutions</a>.`
      }
    })
}

module.exports = config
