import Link from '@docusaurus/Link'
import Layout from '@theme/Layout'
import {
  ArrowRight,
  BookOpen,
  Building2,
  Check,
  Copy,
  FolderGit2,
  GitBranch,
  GitMerge,
  Layers3,
  Package,
  PackageCheck,
  RefreshCw,
  Search,
  Terminal,
  Wrench
} from 'lucide-react'
import { useState } from 'react'

import styles from './index.module.css'

const quickStartCommand = `luna init
luna sources add github lunapack lunarisdigitalsolutions/lunapack
luna discover
luna install dotnet-project --dry-run

# Apply after reviewing the plan
luna install dotnet-project
luna outdated
luna update dotnet-project --dry-run`

const authorCommand = `mkdir my-pack

# Add my-pack/pack.yml and managed content
luna sources add local my-packs .
luna validate my-pack`

const solutionSteps = [
  {
    icon: Package,
    title: 'Pack',
    detail: 'A versioned folder holds the setup your team wants to reuse.'
  },
  {
    icon: Terminal,
    title: 'CLI',
    detail: 'LunaPack discovers, previews, installs, and updates that pack.'
  },
  {
    icon: FolderGit2,
    title: 'Project',
    detail: 'The project records what it requested and what LunaPack resolved.'
  }
]

const comparisonPoints = [
  {
    icon: Package,
    title: 'Not a package manager',
    detail:
      'NuGet, npm, pip, Cargo, and similar tools resolve libraries for an application. LunaPack delivers the project foundation around those libraries: configuration, tooling, documentation, and other managed files, with a version your project can update.'
  },
  {
    icon: GitBranch,
    title: 'More than workflow reuse',
    detail:
      'GitHub Actions can call actions and reusable workflows from other repositories, while Azure Pipelines can load templates from another Git repository. LunaPack complements those mechanisms by versioning and applying the broader project foundation around them, with previewable updates across managed files.'
  },
  {
    icon: Layers3,
    title: 'Around IaC modules',
    detail:
      'Terraform and Bicep modules can live in central registries and be consumed by deployments. LunaPack solves the surrounding project setup instead: main files, linting, formatting, CI, documentation, and repository conventions, versioned and updated together.'
  },
  {
    icon: FolderGit2,
    title: 'More than a template',
    detail:
      'Project templates and generators create a starting snapshot. LunaPack keeps the foundation connected to a versioned source, so teams can inspect and apply updates after project creation.'
  }
]

const lifecycleSteps = [
  {
    number: '01',
    title: 'Choose a pack',
    detail:
      'Use a local folder or Git source that already has the setup you need.'
  },
  {
    number: '02',
    title: 'Initialize a project',
    detail:
      'Create LunaPack state, discover a release, then preview an install.'
  },
  {
    number: '03',
    title: 'Customize it',
    detail:
      'Pass parameters and choose a project-relative destination when needed.'
  },
  {
    number: '04',
    title: 'Receive updates',
    detail: 'Check what is outdated, review the plan, then apply the update.'
  }
]

const benefits = [
  'Start new projects without rebuilding the same setup.',
  'Give every repository the standards your team already chose.',
  'Update shared foundations instead of copying new templates.',
  'Make onboarding a repeatable command, not a repository hunt.'
]

const featuredPacks = [
  {
    title: 'Repository basics',
    detail:
      'Use gitignore-general and license-mit to establish clean, portable repository defaults.'
  },
  {
    title: 'Team standards',
    detail:
      'Use clean-code-guidelines, C# guidance, and ADR templates to share engineering practices.'
  },
  {
    title: 'Pull request quality',
    detail:
      'Use Commitlint and GitHub Actions packs to make review checks repeatable.'
  },
  {
    title: '.NET foundations',
    detail:
      'Use the .NET packs for shared build, SDK, package, and formatting policy when your project is .NET.'
  }
]

export default function Home() {
  const [copied, setCopied] = useState(false)
  const [authorCommandCopied, setAuthorCommandCopied] = useState(false)

  async function copyInstallationCommand() {
    await navigator.clipboard.writeText(quickStartCommand)
    setCopied(true)
    window.setTimeout(() => setCopied(false), 2000)
  }

  async function copyAuthorCommand() {
    await navigator.clipboard.writeText(authorCommand)
    setAuthorCommandCopied(true)
    window.setTimeout(() => setAuthorCommandCopied(false), 2000)
  }

  return (
    <Layout
      title='Versioned engineering foundations'
      description='Versioned engineering foundations for real projects.'
    >
      <main className={styles.home}>
        <section className={styles.hero}>
          <div className={styles.heroInner}>
            <div className={styles.heroCopy}>
              <div className={styles.eyebrow}>
                LunaPack for engineering foundations
              </div>
              <h1>Start new projects without recreating the setup.</h1>
              <p className={styles.heroLead}>
                LunaPack uses the Luna CLI to install reusable, versioned packs
                so your team can start with its standards instead of copying
                another repository.
              </p>
              <div className={styles.heroActions}>
                <Link className='button button--primary' to='/developer/'>
                  Get started <ArrowRight aria-hidden='true' size={18} />
                </Link>
                <Link
                  className='button button--secondary'
                  to='https://github.com/lunarisdigitalsolutions/lunapack/tree/main/projects/packs'
                >
                  Browse packs
                </Link>
              </div>
              <dl className={styles.heroFacts}>
                <div>
                  <dt>Problem</dt>
                  <dd>Setup drifts</dd>
                </div>
                <div>
                  <dt>Answer</dt>
                  <dd>Versioned packs</dd>
                </div>
                <div>
                  <dt>Start</dt>
                  <dd>Preview and update</dd>
                </div>
              </dl>
            </div>
            <div
              className={styles.heroVisual}
              aria-label='Example LunaPack workflow'
            >
              <div className={styles.terminalHeader}>
                <span>new project, shared setup</span>
                <span className={styles.terminalStatus}>ready to use</span>
              </div>
              <div className={styles.terminalBody}>
                <div className={styles.terminalLine}>
                  <span className={styles.prompt}>$</span> luna init
                </div>
                <div className={styles.terminalOutput}>
                  Created lunapack.yml
                </div>
                <div className={styles.terminalLine}>
                  <span className={styles.prompt}>$</span> luna sources add
                  github lunapack lunarisdigitalsolutions/lunapack
                </div>
                <div className={styles.packageList}>
                  <span>
                    <GitBranch aria-hidden='true' size={16} /> source connected
                  </span>
                  <span>
                    <Search aria-hidden='true' size={16} /> packs available
                  </span>
                </div>
                <div className={styles.terminalLine}>
                  <span className={styles.prompt}>$</span> luna install
                  dotnet-project
                </div>
                <div className={styles.terminalOutput}>
                  Preview before changing the project with --dry-run.
                </div>
              </div>
            </div>
          </div>
        </section>

        <section className={styles.problem}>
          <div className={styles.sectionInner}>
            <div className={styles.sectionLabel}>Why LunaPack</div>
            <div className={styles.sectionIntro}>
              <h2>Every project starts differently. It should not.</h2>
              <p>
                Technical leads know the routine: copy a starter repository,
                repair its pipeline, repeat security settings, and explain the
                project shape again. The cost compounds as each copy begins to
                drift.
              </p>
            </div>
            <div className={styles.problemGrid}>
              <div>
                <span className={styles.problemNumber}>01</span>
                <h3>Templates drift</h3>
                <p>
                  Copied repositories lose their connection to the setup that
                  created them.
                </p>
              </div>
              <div>
                <span className={styles.problemNumber}>02</span>
                <h3>Setup repeats</h3>
                <p>
                  Pipelines, security configuration, and docs get rebuilt for
                  every project.
                </p>
              </div>
              <div>
                <span className={styles.problemNumber}>03</span>
                <h3>Teams slow down</h3>
                <p>
                  New contributors spend time finding the right starting point.
                </p>
              </div>
            </div>
          </div>
        </section>

        <section className={styles.solution}>
          <div className={styles.sectionInner}>
            <div className={styles.sectionHeader}>
              <div>
                <div className={styles.sectionLabel}>The approach</div>
                <h2>Pack to CLI to project.</h2>
              </div>
              <p>
                A pack contains reusable project setup. LunaPack applies it. The
                project keeps the selected version so changes stay intentional.
              </p>
            </div>
            <div className={styles.solutionGrid}>
              {solutionSteps.map(({ icon: Icon, title, detail }) => (
                <article className={styles.solutionCard} key={title}>
                  <Icon aria-hidden='true' size={26} strokeWidth={1.8} />
                  <h3>{title}</h3>
                  <p>{detail}</p>
                </article>
              ))}
            </div>
          </div>
        </section>

        <section className={styles.comparison}>
          <div className={styles.sectionInner}>
            <div className={styles.sectionHeader}>
              <div>
                <div className={styles.sectionLabel}>A different layer</div>
                <h2>LunaPack connects the setup between your tools.</h2>
              </div>
              <p>
                Package managers, workflow reuse, IaC modules, and project
                templates each solve a focused problem. LunaPack assembles the
                broader project foundation around them, then keeps its managed
                files versioned and updateable.
              </p>
            </div>
            <div className={styles.comparisonGrid}>
              {comparisonPoints.map(({ icon: Icon, title, detail }) => (
                <article className={styles.comparisonCard} key={title}>
                  <Icon aria-hidden='true' size={24} />
                  <h3>{title}</h3>
                  <p>{detail}</p>
                </article>
              ))}
            </div>
          </div>
        </section>

        <section className={styles.workflow}>
          <div className={styles.sectionInner}>
            <div className={styles.sectionLabel}>How it works</div>
            <h2>Use a pack in four small steps.</h2>
            <div className={styles.lifecycleGrid}>
              {lifecycleSteps.map(({ number, title, detail }) => (
                <article className={styles.lifecycleStep} key={number}>
                  <span>{number}</span>
                  <h3>{title}</h3>
                  <p>{detail}</p>
                </article>
              ))}
            </div>
          </div>
        </section>

        <section className={styles.commandSection}>
          <div className={styles.sectionInner}>
            <div className={styles.commandGrid}>
              <div>
                <div className={styles.sectionLabel}>Quick start</div>
                <h2>Get a useful project foundation in minutes.</h2>
                <p>
                  Install the LunaPack release, put the executable on your path,
                  then initialize a project and preview the pack you want.
                </p>
                <p className={styles.commandNote}>
                  Remove <code>--dry-run</code> when the preview is ready to
                  apply.
                </p>
                <p className={styles.installationLink}>
                  <Link to='/developer/installation'>
                    Install via npm, NuGet, or Docker{' '}
                    <ArrowRight aria-hidden='true' size={18} />
                  </Link>
                </p>
              </div>
              <div className={styles.commandPanel}>
                <div className={styles.commandPanelHeader}>
                  <span>shell</span>
                  <button
                    className={styles.copyButton}
                    type='button'
                    onClick={copyInstallationCommand}
                    aria-label={
                      copied ? 'Quick start copied' : 'Copy quick start'
                    }
                    title={copied ? 'Copied' : 'Copy quick start'}
                  >
                    {copied ? (
                      <Check aria-hidden='true' size={18} />
                    ) : (
                      <Copy aria-hidden='true' size={18} />
                    )}
                  </button>
                </div>
                <pre>
                  <code>{quickStartCommand}</code>
                </pre>
              </div>
            </div>
          </div>
        </section>

        <section className={styles.featuredPacks}>
          <div className={styles.sectionInner}>
            <div className={styles.sectionHeader}>
              <div>
                <div className={styles.sectionLabel}>Featured packs</div>
                <h2>Start with foundations for your stack.</h2>
              </div>
              <p>
                The catalog includes repository basics, team standards, and CI
                checks alongside stack-specific packs. Pick one or compose
                several as your project needs them.
              </p>
            </div>
            <div className={styles.featuredGrid}>
              {featuredPacks.map(({ title, detail }) => (
                <article className={styles.featuredPack} key={title}>
                  <PackageCheck aria-hidden='true' size={22} />
                  <h3>{title}</h3>
                  <p>{detail}</p>
                </article>
              ))}
            </div>
            <Link
              className='button button--secondary'
              to='https://github.com/lunarisdigitalsolutions/lunapack/tree/main/projects/packs'
            >
              Browse packs <ArrowRight aria-hidden='true' size={18} />
            </Link>
          </div>
        </section>

        <section className={styles.updates}>
          <div className={styles.sectionInner}>
            <div className={styles.sectionHeader}>
              <div>
                <div className={styles.sectionLabel}>Stay current</div>
                <h2>Update foundations without starting over.</h2>
              </div>
              <p>
                Copied templates stop evolving the moment they land in a
                repository. LunaPack keeps source versions and managed-file
                digests, so you can inspect available updates, preview changes,
                and apply them intentionally.
              </p>
            </div>
            <div className={styles.updateGrid}>
              <article>
                <RefreshCw aria-hidden='true' size={24} />
                <h3>Review before applying</h3>
                <p>
                  Run <code>luna outdated</code>, then use{' '}
                  <code>luna update --dry-run</code> to inspect the complete
                  plan before files change.
                </p>
              </article>
              <article>
                <GitMerge aria-hidden='true' size={24} />
                <h3>Choose how content combines</h3>
                <p>
                  Packs can copy with overwrite, fail, skip, or backup behavior,
                  or merge line sets, marker-bounded sections, and JSON content.
                </p>
              </article>
            </div>
          </div>
        </section>

        <section className={styles.authoring}>
          <div className={styles.sectionInner}>
            <div className={styles.commandGrid}>
              <div>
                <div className={styles.sectionLabel}>Create your own pack</div>
                <h2>Turn your team&apos;s setup into a source.</h2>
                <p>
                  A pack is a versioned folder with a <code>pack.yml</code> file
                  and the content it manages. Start locally, test it in a
                  throwaway project, then publish it to Git when it is ready.
                </p>
                <Link
                  className='button button--primary'
                  to='/developer/packs/tutorials/first-pack'
                >
                  Create a first pack <Wrench aria-hidden='true' size={18} />
                </Link>
              </div>
              <div className={styles.authorPanel}>
                <div className={styles.commandPanelHeader}>
                  <span>shell</span>
                  <button
                    className={styles.copyButton}
                    type='button'
                    onClick={copyAuthorCommand}
                    aria-label={
                      authorCommandCopied
                        ? 'Pack source command copied'
                        : 'Copy pack source command'
                    }
                    title={authorCommandCopied ? 'Copied' : 'Copy command'}
                  >
                    {authorCommandCopied ? (
                      <Check aria-hidden='true' size={18} />
                    ) : (
                      <Copy aria-hidden='true' size={18} />
                    )}
                  </button>
                </div>
                <pre>
                  <code>{authorCommand}</code>
                </pre>
              </div>
            </div>
          </div>
        </section>

        <section className={styles.benefits}>
          <div className={styles.sectionInner}>
            <div className={styles.sectionHeader}>
              <div>
                <div className={styles.sectionLabel}>What changes</div>
                <h2>Make the good start the default start.</h2>
              </div>
            </div>
            <ul>
              {benefits.map((benefit) => (
                <li key={benefit}>
                  <Check aria-hidden='true' size={20} />
                  <span>{benefit}</span>
                </li>
              ))}
            </ul>
          </div>
        </section>

        <section className={styles.community}>
          <div className={styles.sectionInner}>
            <div>
              <div className={styles.sectionLabel}>Open source</div>
              <h2>Use it, discuss it, improve it.</h2>
            </div>
            <div className={styles.communityLinks}>
              <a href='https://github.com/lunarisdigitalsolutions/lunapack'>
                <BookOpen aria-hidden='true' size={20} /> GitHub
              </a>
              <a href='https://github.com/lunarisdigitalsolutions/lunapack/discussions'>
                <Layers3 aria-hidden='true' size={20} /> Discussions
              </a>
              <a href='https://github.com/lunarisdigitalsolutions/lunapack/blob/main/CONTRIBUTING.md'>
                <Wrench aria-hidden='true' size={20} /> Contribution guide
              </a>
            </div>
          </div>
        </section>

        <section className={styles.callToAction}>
          <div className={styles.sectionInner}>
            <div>
              <div className={styles.sectionLabel}>Ready to start?</div>
              <h2>Pick a pack. Start the next project well.</h2>
            </div>
            <Link className='button button--secondary' to='/developer/'>
              Browse documentation <ArrowRight aria-hidden='true' size={18} />
            </Link>
          </div>
        </section>

        <section className={styles.company}>
          <div className={styles.sectionInner}>
            <Building2 aria-hidden='true' size={30} />
            <div>
              <div className={styles.sectionLabel}>Supported by Lunaris</div>
              <h2>Free and open source, backed by practical expertise.</h2>
              <p>
                <a href='https://lunaris.digital'>Lunaris Digital Solutions</a>{' '}
                is the company behind LunaPack and supports creation of the CLI
                and its packs. LunaPack will remain free and open source.
                Consulting and training are available for teams adopting or
                authoring packs.
              </p>
            </div>
            <a
              className='button button--secondary'
              href='https://lunaris.digital'
            >
              Work with Lunaris <ArrowRight aria-hidden='true' size={18} />
            </a>
          </div>
        </section>
      </main>
    </Layout>
  )
}
