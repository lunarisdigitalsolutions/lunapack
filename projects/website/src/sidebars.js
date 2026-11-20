/** @type {import('@docusaurus/plugin-content-docs').SidebarsConfig} */
const sidebars = {
  developerSidebar: [
    {
      type: 'doc',
      id: 'index',
      label: 'What is LunaPack?'
    },
    {
      type: 'category',
      label: 'Get started',
      collapsed: false,
      items: [
        'installation',
        'luna-cli-skill',
        'sources',
        'evaluate-packs',
        'install-packs',
        'copy-files-from-git'
      ]
    },
    {
      type: 'category',
      label: 'Everyday use',
      items: [
        'update-packs',
        'parameters-and-variables',
        'remap-targets',
        'lifecycle-hooks'
      ]
    },
    {
      type: 'category',
      label: 'Advanced use',
      items: [
        'manage-links',
        'advanced/approve-external-sources',
        'advanced/git-source-behavior',
        'advanced/adopt-existing-files',
        'advanced/install-multiple-packs',
        'advanced/audit-and-recover',
        {
          type: 'category',
          label: 'Automation',
          items: ['automation', 'advanced/publish-private-packs']
        }
      ]
    },
    {
      type: 'category',
      label: 'Author packs',
      items: [
        'packs/index',
        {
          type: 'category',
          label: 'Tutorials',
          items: [
            'packs/tutorials/first-pack',
            'packs/tutorials/create-a-pack-version'
          ]
        },
        {
          type: 'category',
          label: 'Core authoring',
          items: [
            'packs/how-to/add-managed-content',
            'packs/how-to/add-conditional-files',
            'packs/how-to/release-a-pack'
          ]
        },
        {
          type: 'category',
          label: 'Advanced authoring',
          items: [
            'packs/how-to/merge-managed-content',
            'packs/how-to/compose-packs',
            'packs/how-to/use-external-pack-sources',
            'packs/how-to/use-scriban-templates',
            'packs/how-to/use-pack-scripts'
          ]
        },
        {
          type: 'category',
          label: 'Concepts',
          items: [
            'packs/explanation/scope-taxonomy-and-naming',
            'packs/explanation/composition-and-lifecycle',
            'packs/explanation/ownership-and-safety'
          ]
        }
      ]
    },
    {
      type: 'category',
      label: 'Reference',
      items: [
        {
          type: 'category',
          label: 'CLI',
          items: [
            'cli/index',
            'cli/overview',
            {
              type: 'category',
              label: 'Commands',
              items: [
                'cli/commands',
                {
                  type: 'category',
                  label: 'Setup',
                  items: [
                    'cli/commands/init',
                    'cli/commands/config',
                    'cli/commands/doctor',
                    'cli/commands/completions'
                  ]
                },
                {
                  type: 'category',
                  label: 'Sources, trust, and project state',
                  items: [
                    'cli/commands/sources',
                    'cli/commands/trust',
                    'cli/commands/variables',
                    'cli/commands/remap',
                    'cli/commands/links'
                  ]
                },
                {
                  type: 'category',
                  label: 'Catalog and lifecycle',
                  items: [
                    'cli/commands/catalog',
                    'cli/commands/pack',
                    'cli/commands/publish',
                    'cli/commands/lifecycle',
                    'cli/commands/mv'
                  ]
                },
                {
                  type: 'category',
                  label: 'Diagnostics',
                  items: [
                    'cli/commands/audit',
                    'cli/commands/sbom',
                    'cli/commands/hooks'
                  ]
                }
              ]
            },
            {
              type: 'category',
              label: 'Configuration and state',
              items: [
                'cli/configuration-reference',
                'cli/configuration',
                'cli/manifests',
                'cli/lock-file',
                'cli/links',
                'cli/trust-and-scripts',
                'cli/environment'
              ]
            }
          ]
        },
        {
          type: 'category',
          label: 'Files and schemas',
          items: [
            'packs/reference/manifest',
            'packs/reference/catalog-layout',
            'packs/reference/project-documents'
          ]
        }
      ]
    },
    {
      type: 'category',
      label: 'Help and security',
      items: [
        'troubleshooting',
        {
          type: 'doc',
          id: 'threat-model',
          label: 'Usage threat model'
        }
      ]
    }
  ]
}

module.exports = sidebars
