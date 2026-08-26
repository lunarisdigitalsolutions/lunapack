module.exports = {
  extends: ['@commitlint/config-conventional'],
  rules: {
    'header-max-length': [1, 'always', 144],
    'subject-case': [2, 'always', ['sentence-case', 'start-case']],
    'type-case': [2, 'always', ['lower-case']],
    'type-enum': [
      2,
      'always',
      ['build', 'ci', 'docs', 'feat', 'fix', 'revert', 'release']
    ],
    'scope-case': [2, 'always', ['lower-case']],
    'scope-enum': [
      2,
      'always',
      { scopes: [null, 'frontend', 'backend'], delimiters: [','] }
    ]
  }
}
