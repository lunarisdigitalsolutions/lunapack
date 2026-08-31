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
      {
        scopes: [
          null{{ if scopes contains "api" }},
          'api'{{ end }}{{ if scopes contains "backend" }},
          'backend'{{ end }}{{ if scopes contains "build" }},
          'build'{{ end }}{{ if scopes contains "cli" }},
          'cli'{{ end }}{{ if scopes contains "docs" }},
          'docs'{{ end }}{{ if scopes contains "frontend" }},
          'frontend'{{ end }}{{ if scopes contains "infrastructure" }},
          'infrastructure'{{ end }}{{ if scopes contains "packs" }},
          'packs'{{ end }}{{ if scopes contains "schema" }},
          'schema'{{ end }}{{ if scopes contains "security" }},
          'security'{{ end }}{{ if scopes contains "tests" }},
          'tests'{{ end }}{{ if scopes contains "website" }},
          'website'{{ end }}
        ],
        delimiters: [',']
      }
    ]
  }
}
