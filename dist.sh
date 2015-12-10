#!/usr/bin/env bash

cd "$(dirname $0)"

(
    cd Maxel/bin/
    mv Release "Maxel-$TRAVIS_TAG"
    zip -r "Maxel-$TRAVIS_TAG.zip" "Maxel-$TRAVIS_TAG"
)
