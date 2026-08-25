const config = require('prettier-config-standard')

module.exports = {
  ...config,
  singleQuote: true,
  overrides: [
    {
      files: ['*.yml', '*.yaml'],
      options: {
        quoteProps: 'consistent',
        useTabs: false,
        tabWidth: 2
      }
    }
  ]
}
