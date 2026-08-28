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
      label: 'Start here',
      collapsed: false,
      items: [
        'installation',
        'sources',
        'install-packs',
        'copy-files-from-git',
        'update-packs'
      ]
    },
    {
      type: 'category',
      label: 'Advanced topics',
      items: [
        'lifecycle-hooks',
        'parameters-and-variables',
        'remap-targets',
        'manage-links'
      ]
    },
    {
      type: 'category',
      label: 'CLI reference',
      items: [
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
      label: 'Pack guide',
      items: [
        'packs/index',
        {
          type: 'category',
          label: 'Tutorials',
          items: ['packs/tutorials/first-pack']
        },
        {
          type: 'category',
          label: 'How-to guides',
          items: [
            'packs/how-to/use-scriban-templates',
            'packs/how-to/release-a-pack'
          ]
        },
        {
          type: 'category',
          label: 'Reference',
          items: [
            'packs/reference/manifest',
            'packs/reference/project-documents'
          ]
        },
        {
          type: 'category',
          label: 'Explanation',
          items: [
            'packs/explanation/composition-and-lifecycle',
            'packs/explanation/ownership-and-safety'
          ]
        }
      ]
    },
    {
      type: 'category',
      label: 'Operate',
      items: ['troubleshooting', 'threat-model']
    },
    {
      type: 'category',
      label: 'Contribute',
      items: ['contributing', 'architecture', 'release-process']
    }
  ]
}

module.exports = sidebars
