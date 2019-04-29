pipeline {
  agent { 
      label 'low-side'
  }

  environment {
      M3T4C0_REGISTRY     = credentials('metaco-registry')
      DOCKER_REGISTRY = 'registry.internal.m3t4c0.com'
      DOCKER_REGISTRY_BETA = 'registry.metaco.network'
      DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE = credentials('DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE')
      DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE = ('DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE_NBXPLORER')
      DOCKER_CONTENT_TRUST_REPOSITORY = 'registry.metaco.network/silo/nbxplorer'
      DOCKER_SIGNER_IMG = 'registry.internal.m3t4c0.com/devops/docker:signer'
      DOCKER_CONTENT_TRUST_SERVER = 'https://registry.metaco.network:4443'
      DOCKER_IMG = 'registry.internal.m3t4c0.com/silo/nbxplorer'
      DOCKER_IMG_BETA = 'registry.metaco.network/silo-beta/nbxplorer'
  }

  parameters {
    booleanParam(name: 'DOCKER_PROD', defaultValue: false, description: 'Deploy signed images to prod?')
    booleanParam(name: 'DOCKER_PROD_BETA', defaultValue: false, description: 'Deploy images to BETA channels?')
    booleanParam(name: 'DOCKER_BUILD', defaultValue: false, description: 'Shall build the docker images?')
  }

  options {
    buildDiscarder(logRotator(numToKeepStr:'10'))
    // timeout(time: 5, unit: 'MINUTES')
    ansiColor('xterm')
  }
  stages {
    stage('Deploy Docker image') {
      when {
        anyOf {
          expression { BRANCH_NAME ==~ /(master|development)/ }
          expression { params.DOCKER_BUILD == true }
        }
      }
      steps {
          script {
            env.DOCKER_TAG = env.BRANCH_NAME.replace("/","-")
          }
          sh '''
            docker login -u ${M3T4C0_REGISTRY_USR} -p ${M3T4C0_REGISTRY_PSW} ${DOCKER_REGISTRY}
            docker build -t ${DOCKER_IMG}:${DOCKER_TAG} -f Dockerfile.linuxamd64 .
            docker push ${DOCKER_IMG}:${DOCKER_TAG}
            '''
      }
    }

    stage('Build Docker tag') {
      when { tag "*" }
      steps {
        sh '''
          docker login -u ${M3T4C0_REGISTRY_USR} -p ${M3T4C0_REGISTRY_PSW} ${DOCKER_REGISTRY}
          docker build -t ${DOCKER_IMG}:${TAG_NAME} -f Dockerfile.linuxamd64 .
          docker push ${DOCKER_IMG}:${TAG_NAME}
        '''
      }
    }

    stage('Build Docker for beta channels') {
      when { 
        allOf {
          tag "*" 
          expression { params.DOCKER_PROD_BETA == true }
        }
      }
      
      steps {
        sh '''
          docker login -u ${M3T4C0_REGISTRY_USR} -p ${M3T4C0_REGISTRY_PSW} ${DOCKER_REGISTRY_BETA}
          docker build -t ${DOCKER_IMG_BETA}:${TAG_NAME} -f Dockerfile.linuxamd64 .
          docker push ${DOCKER_IMG_BETA}:${TAG_NAME}
        '''
      }
    }

    stage('Deploy Docker Sign Sign') {
      when { 
        allOf {
          expression { params.DOCKER_PROD == true }
          tag 'v*'
        }
      }

      steps {
          sh '''
              docker login -u ${M3T4C0_REGISTRY_USR} -p ${M3T4C0_REGISTRY_PSW} ${DOCKER_CONTENT_TRUST_REGISTRY}
              docker build -t ${DOCKER_BUILDER_IMG}:${TAG_NAME} -f Dockerfile.linuxamd64 .
              docker run --rm \
              -v /var/run/docker.sock:/var/run/docker.sock \
              -e DOCKER_CONTENT_TRUST=1 \
              -e DOCKER_CONTENT_TRUST_SERVER="${DOCKER_CONTENT_TRUST_SERVER}" \
              -e DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE="${DOCKER_CONTENT_TRUST_ROOT_PASSPHRASE}" \
              -e DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE="${DOCKER_CONTENT_TRUST_REPOSITORY_PASSPHRASE}" \
              ${DOCKER_SIGNER_IMG} docker push ${DOCKER_CONTENT_TRUST_REPOSITORY}:${TAG_NAME}
            '''
      }
    }
  }
}
