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
        'automation'
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
            'cli/commands',
            'cli/configuration',
            'cli/links',
            'cli/manifests',
            'cli/trust-and-scripts'
          ]
        },
        {
          type: 'category',
          label: 'Files and schemas',
          items: [
            'cli/environment',
            'cli/lock-file',
            'packs/reference/manifest',
            'packs/reference/project-documents'
          ]
        }
      ]
    },
    {
      type: 'category',
      label: 'Help and security',
      items: ['troubleshooting', 'threat-model']
    }
  ]
}

module.exports = sidebars
